using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;

namespace BeatSaberPlus_NoteTweaker
{
    /// <summary>
    /// NoteTweaker Module
    /// </summary>
    public class NoteTweaker : CP_SDK.ModuleBase<NoteTweaker>
    {
        internal const string IMPORT_FOLDER = "UserData/BeatSaberPlus/NoteTweaker/Import/";
        internal const string EXPORT_FOLDER = "UserData/BeatSaberPlus/NoteTweaker/Export/";

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override CP_SDK.EIModuleBaseType             Type                => CP_SDK.EIModuleBaseType.Integrated;
        public override string                              Name                => "Note Tweaker";
        public override string                              Description         => "Customize base notes!";
        public override string                              DocumentationURL    => "https://github.com/hardcpp/BeatSaberPlus/wiki#note-tweaker";
        public override bool                                UseChatFeatures     => false;
        public override bool                                IsEnabled           { get => NTConfig.Instance.Enabled; set { NTConfig.Instance.Enabled = value; NTConfig.Instance.Save(); } }
        public override CP_SDK.EIModuleBaseActivationType   ActivationType      => CP_SDK.EIModuleBaseActivationType.OnMenuSceneLoaded;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private UI.SettingsLeftView     m_SettingsLeftView  = null;
        private UI.SettingsMainView     m_SettingsMainView  = null;
        private UI.SettingsRightView    m_SettingsRightView = null;

        private int? m_BackupProfileIndex = null;

        internal static object FileSession { get; private set; } = new object();
        private static Task m_LastProfileFileWork = Task.CompletedTask;

        internal sealed class ProfileFileResult
        {
            internal List<string> Names;
            internal string Raw;
            internal NTConfig._Profile Profile;
            internal Exception Error;
            internal bool Missing;
            internal bool Invalid;
            internal bool ParseOnOwner;
        }

        internal sealed class ProfileFileOperation
        {
            internal readonly Task Completion;
            internal readonly ProfileFileResult Result;

            internal ProfileFileOperation(Task p_Completion, ProfileFileResult p_Result)
            {
                Completion = p_Completion;
                Result = p_Result;
            }
        }

        private enum ProfileFileAction { Initialize, Scan, Import, Export }
        private sealed class OwnerParsingRequired : Exception { }

        private sealed class ProfileColorConverter : CP_SDK.Config.JsonConverters.ColorConverter
        {
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var l_Token = serializer.Deserialize(reader);
                // Formatting numeric, date, or custom values must stay with the caller.
                if (l_Token != null && !(l_Token is JObject) && !(l_Token is JArray)
                    && !(l_Token is string) && !(l_Token is bool))
                    throw new OwnerParsingRequired();

                var l_Serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    ContractResolver = new DefaultContractResolver(),
                    CheckAdditionalContent = true
                });
                using (var l_StringReader = new StringReader(l_Token.ToString()))
                using (var l_Reader = new JsonTextReader(l_StringReader))
                    return l_Serializer.Deserialize<Color>(l_Reader);
            }
        }

        private sealed class ProfileFileRequest
        {
            internal readonly ProfileFileResult Result = new ProfileFileResult();
            internal ProfileFileAction Action;
            internal string Folder;
            internal string ExportFolder;
            internal string FileName;
            internal string Raw;
            internal NTConfig._Profile Profile;
            internal bool ParseOnWorker;

            internal void Run()
            {
                try
                {
                    switch (Action)
                    {
                        case ProfileFileAction.Initialize:
                            if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
                            if (!Directory.Exists(ExportFolder)) Directory.CreateDirectory(ExportFolder);
                            break;

                        case ProfileFileAction.Scan:
                            Result.Names = new List<string>();
                            foreach (var l_File in Directory.GetFiles(Folder, "*.bspnt"))
                                Result.Names.Add(Path.GetFileNameWithoutExtension(l_File));
                            break;

                        case ProfileFileAction.Import:
                            if (!File.Exists(FileName))
                            {
                                Result.Missing = true;
                                break;
                            }
                            Result.Raw = File.ReadAllText(FileName, Encoding.Unicode);
                            Result.ParseOnOwner = !ParseOnWorker;
                            if (ParseOnWorker)
                            {
                                try
                                {
                                    var l_Serializer = CreateProfileSerializer();
                                    using (var l_StringReader = new StringReader(Result.Raw))
                                    using (var l_Reader = new JsonTextReader(l_StringReader))
                                        Result.Profile = l_Serializer.Deserialize<NTConfig._Profile>(l_Reader);
                                    Result.Profile.Name += " (Imported)";
                                }
                                catch (Exception l_Exception)
                                {
                                    for (var l_Error = l_Exception; l_Error != null; l_Error = l_Error.InnerException)
                                    {
                                        if (l_Error is OwnerParsingRequired)
                                        {
                                            Result.ParseOnOwner = true;
                                            break;
                                        }
                                    }
                                    Result.Invalid = !Result.ParseOnOwner;
                                }
                            }
                            break;

                        case ProfileFileAction.Export:
                            var l_Raw = Raw;
                            if (Profile != null)
                            {
                                var l_Serializer = CreateProfileSerializer();
                                l_Serializer.Formatting = Formatting.Indented;
                                using (var l_StringWriter = new StringWriter(new StringBuilder(256), CultureInfo.InvariantCulture))
                                {
                                    using (var l_Writer = new JsonTextWriter(l_StringWriter))
                                    {
                                        l_Writer.Formatting = l_Serializer.Formatting;
                                        l_Serializer.Serialize(l_Writer, Profile, null);
                                    }
                                    l_Raw = l_StringWriter.ToString();
                                }
                            }
                            var l_FileName = string.Concat(FileName.Split(Path.GetInvalidFileNameChars()));
                            File.WriteAllText(Path.Combine(Folder, l_FileName), l_Raw, Encoding.Unicode);
                            break;
                    }
                }
                catch (Exception l_Exception) { Result.Error = l_Exception; }
            }
        }

        private static JsonSerializer CreateProfileSerializer()
        {
            return JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver(),
                Converters = new JsonConverter[] { new ProfileColorConverter() },
                CheckAdditionalContent = true
            });
        }

        private static Task RunProfileFileWork(ProfileFileRequest p_Request, Task p_Predecessor)
        {
            return Task.Run(async () =>
            {
                try { await p_Predecessor.ConfigureAwait(false); }
                catch (Exception) { }
                p_Request.Run();
            });
        }

        // Admission is owner-only; keep every accepted write ordered until its physical completion.
        private static ProfileFileOperation QueueProfileFileWork(ProfileFileRequest p_Request)
        {
            var l_Completion = RunProfileFileWork(p_Request, m_LastProfileFileWork);
            m_LastProfileFileWork = l_Completion;
            return new ProfileFileOperation(l_Completion, p_Request.Result);
        }

        internal static ProfileFileOperation BeginProfileScan()
        {
            return QueueProfileFileWork(new ProfileFileRequest
            {
                Action = ProfileFileAction.Scan,
                Folder = Path.GetFullPath(IMPORT_FOLDER)
            });
        }

        internal static ProfileFileOperation BeginProfileImport(string p_Name)
        {
            return QueueProfileFileWork(new ProfileFileRequest
            {
                Action = ProfileFileAction.Import,
                FileName = Path.GetFullPath(IMPORT_FOLDER + p_Name + ".bspnt"),
                ParseOnWorker = JsonConvert.DefaultSettings == null
            });
        }

        internal static ProfileFileOperation BeginProfileExport(NTConfig._Profile p_Profile)
        {
            var l_Request = new ProfileFileRequest { Action = ProfileFileAction.Export };
            if (JsonConvert.DefaultSettings == null && p_Profile.GetType() == typeof(NTConfig._Profile))
            {
                l_Request.Profile = new NTConfig._Profile
                {
                    Name = p_Profile.Name,
                    NotesScale = p_Profile.NotesScale,
                    NotesShowPrecisonDots = p_Profile.NotesShowPrecisonDots,
                    NotesPrecisonDotsScale = p_Profile.NotesPrecisonDotsScale,
                    ArrowsScale = p_Profile.ArrowsScale,
                    ArrowsIntensity = p_Profile.ArrowsIntensity,
                    ArrowsOverrideColors = p_Profile.ArrowsOverrideColors,
                    ArrowsLColor = p_Profile.ArrowsLColor,
                    ArrowsRColor = p_Profile.ArrowsRColor,
                    DotsScale = p_Profile.DotsScale,
                    DotsIntensity = p_Profile.DotsIntensity,
                    DotsOverrideColors = p_Profile.DotsOverrideColors,
                    DotsLColor = p_Profile.DotsLColor,
                    DotsRColor = p_Profile.DotsRColor,
                    BombsScale = p_Profile.BombsScale,
                    BombsOverrideColor = p_Profile.BombsOverrideColor,
                    BombsColor = p_Profile.BombsColor,
                    ArcsIntensity = p_Profile.ArcsIntensity,
                    ArcsHaptics = p_Profile.ArcsHaptics,
                    BurstNotesDotsScale = p_Profile.BurstNotesDotsScale
                };
            }
            else
            {
                l_Request.Raw = JsonConvert.SerializeObject(p_Profile, Formatting.Indented,
                    new JsonConverter[] { new CP_SDK.Config.JsonConverters.ColorConverter() });
            }
            l_Request.FileName = CP_SDK.Misc.Time.UnixTimeNow() + "_" + p_Profile.Name + ".bspnt";
            l_Request.Folder = Path.GetFullPath(EXPORT_FOLDER);
            return QueueProfileFileWork(l_Request);
        }


        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Enable the Module
        /// </summary>
        protected override void OnEnable()
        {
            FileSession = new object();
            CP_SDK_BS.Game.Logic.OnSceneChange += OnSceneChange;

            /// Trigger a Set config
            OnSceneChange(CP_SDK_BS.Game.Logic.ESceneType.Menu);

            try
            {
                QueueProfileFileWork(new ProfileFileRequest
                {
                    Action = ProfileFileAction.Initialize,
                    Folder = Path.GetFullPath(IMPORT_FOLDER),
                    ExportFolder = Path.GetFullPath(EXPORT_FOLDER)
                });
            }
            catch (System.Exception)
            {

            }
        }
        /// <summary>
        /// Disable the Module
        /// </summary>
        protected override void OnDisable()
        {
            FileSession = new object();
            CP_SDK_BS.Game.Logic.OnSceneChange -= OnSceneChange;

            CP_SDK.UI.UISystem.DestroyUI(ref m_SettingsLeftView);
            CP_SDK.UI.UISystem.DestroyUI(ref m_SettingsMainView);
            CP_SDK.UI.UISystem.DestroyUI(ref m_SettingsRightView);

            /// Restore config
            Patches.PBombNoteController.SetFromConfig(true);
            Patches.PBombNoteController.SetTemp(false, 1f);
            Patches.PBurstSliderGameNoteController.SetFromConfig(true);
            Patches.PBurstSliderGameNoteController.SetTemp(false, 1f);
            Patches.PColorNoteVisuals.SetFromConfig(true);
            Patches.PColorNoteVisuals.SetBlockColorOverride(false, Color.black, Color.black);
            Patches.PGameNoteController.SetFromConfig(true);
            Patches.PGameNoteController.SetTemp(false, 1f);
            Patches.PSliderController.SetFromConfig(true);
            Patches.PSliderHapticFeedbackInteractionEffect.SetFromConfig(true);
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Get Module settings UI
        /// </summary>
        protected override (CP_SDK.UI.IViewController, CP_SDK.UI.IViewController, CP_SDK.UI.IViewController) GetSettingsViewControllersImplementation()
        {
            if (m_SettingsLeftView == null)     m_SettingsLeftView  = CP_SDK.UI.UISystem.CreateViewController<UI.SettingsLeftView>();
            if (m_SettingsMainView == null)     m_SettingsMainView  = CP_SDK.UI.UISystem.CreateViewController<UI.SettingsMainView>();
            if (m_SettingsRightView == null)    m_SettingsRightView = CP_SDK.UI.UISystem.CreateViewController<UI.SettingsRightView>();

            /// Change main view
            return (m_SettingsMainView, m_SettingsLeftView, m_SettingsRightView);
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// List available profiles
        /// </summary>
        /// <returns></returns>
        public List<string> GetAvailableProfiles()
            => NTConfig.Instance.Profiles.Select(x => x.Name).ToList();
        /// <summary>
        /// Switch to profile
        /// </summary>
        /// <param name="p_Index">Profile index</param>
        /// <param name="p_Temporary">Is a temporary change?</param>
        public void SwitchToProfile(int p_Index, bool p_Temporary)
        {
            if (p_Temporary)
                m_BackupProfileIndex = NTConfig.Instance.ActiveProfile;
            else
                m_BackupProfileIndex = null;

            NTConfig.Instance.ActiveProfile = Mathf.Clamp(p_Index, 0, NTConfig.Instance.Profiles.Count);

            Patches.PBombNoteController.SetFromConfig(true);
            Patches.PBurstSliderGameNoteController.SetFromConfig(true);
            Patches.PColorNoteVisuals.SetFromConfig(true);
            Patches.PGameNoteController.SetFromConfig(true);
            Patches.PSliderController.SetFromConfig(true);
            Patches.PSliderHapticFeedbackInteractionEffect.SetFromConfig(true);

            if (!p_Temporary)
                NTConfig.Instance.Save();
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// When the active scene change
        /// </summary>
        /// <param name="p_Scene">New active scene</param>
        private void OnSceneChange(CP_SDK_BS.Game.Logic.ESceneType p_Scene)
        {
            if (p_Scene == CP_SDK_BS.Game.Logic.ESceneType.Playing && m_BackupProfileIndex.HasValue)
                NTConfig.Instance.ActiveProfile = Mathf.Clamp(m_BackupProfileIndex.Value, 0, NTConfig.Instance.Profiles.Count);

            Patches.PBombNoteController.SetFromConfig(true);
            Patches.PBombNoteController.SetTemp(false, 1f);
            Patches.PBurstSliderGameNoteController.SetFromConfig(true);
            Patches.PBurstSliderGameNoteController.SetTemp(false, 1f);
            Patches.PColorNoteVisuals.SetFromConfig(true);
            Patches.PColorNoteVisuals.SetBlockColorOverride(false, Color.black, Color.black);
            Patches.PGameNoteController.SetFromConfig(true);
            Patches.PGameNoteController.SetTemp(false, 1f);
            Patches.PSliderController.SetFromConfig(true);
            Patches.PSliderHapticFeedbackInteractionEffect.SetFromConfig(true);
        }
    }
}
