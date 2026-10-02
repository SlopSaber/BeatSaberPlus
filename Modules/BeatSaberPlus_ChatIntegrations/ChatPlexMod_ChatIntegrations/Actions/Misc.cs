using ChatPlexMod_ChatIntegrations.Models;
using CP_SDK.XUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ChatPlexMod_ChatIntegrations.Actions
{
    internal class MiscRegistration
    {
        internal static void Register()
        {
            ChatIntegrations.RegisterActionType("Misc_Delay",             () => new Misc_Delay());
            ChatIntegrations.RegisterActionType("Misc_PlaySound",         () => new Misc_PlaySound());
            ChatIntegrations.RegisterActionType("Misc_WaitMenuScene",     () => new Misc_WaitMenuScene());
            ChatIntegrations.RegisterActionType("Misc_WaitPlayingScene",  () => new Misc_WaitPlayingScene());
        }
    }

    ////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////

    public class Misc_Delay
        : Interfaces.IAction<Misc_Delay, Models.Actions.Misc_Delay>
    {
        private XUISlider m_Delay                       = null;
        private XUISlider m_DelayMs                     = null;
        private XUIToggle m_PreventNextActionsFailure   = null;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override string Description => "Delay next actions";

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override void BuildUI(Transform p_Parent)
        {
            XUIElements = new IXUIElement[]
            {
                Templates.SettingsVGroup("Delay",
                    XUISlider.Make()
                        .SetMinValue(0.0f).SetMaxValue(1200.0f).SetIncrements(1.0f).SetFormatter(CP_SDK.UI.ValueFormatters.TimeShortBaseSeconds)
                        .SetValue(Model.Delay)
                        .OnValueChanged((_) => OnSettingChanged())
                        .Bind(ref m_Delay),
                    XUISlider.Make()
                        .SetMinValue(0.0f).SetMaxValue(1000.0f).SetIncrements(1.0f).SetFormatter(CP_SDK.UI.ValueFormatters.MillisecondsShort)
                        .SetValue(Model.DelayMs)
                        .OnValueChanged((_) => OnSettingChanged())
                        .Bind(ref m_DelayMs)
                ),

                Templates.SettingsHGroup("Prevent next actions failure",
                    XUIToggle.Make()
                        .SetValue(Model.PreventNextActionFailure)
                        .OnValueChanged((_) => OnSettingChanged())
                        .Bind(ref m_PreventNextActionsFailure)
                ),

                XUIVLayout.Make(
                    XUIText.Make("This actions will delay next actions execution"),
                    XUIText.Make("If prevent next actions failure is enabled,"),
                    XUIText.Make("any failed action won't refund the user")
                )
                .SetBackground(true),
            };

            BuildUIAuto(p_Parent);
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private void OnSettingChanged()
        {
            Model.Delay                     = (uint)m_Delay.Element.GetValue();
            Model.DelayMs                   = (uint)m_DelayMs.Element.GetValue();
            Model.PreventNextActionFailure  = m_PreventNextActionsFailure.Element.GetValue();
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override IEnumerator Eval(Models.EventContext p_Context)
        {
            if (Model.PreventNextActionFailure)
                p_Context.PreventNextActionFailure = true;

            yield return new WaitForSecondsRealtime((float)Model.Delay + (((float)Model.DelayMs) / 1000f));
        }
    }

    ////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////

    public class Misc_PlaySound
        : Interfaces.IAction<Misc_PlaySound, Models.Actions.Misc_PlaySound>
    {
        private XUIDropdown m_Dropdown          = null;
        private XUISlider   m_Volume            = null;
        private XUISlider   m_PitchMin          = null;
        private XUISlider   m_PitchMax          = null;
        private XUIToggle   m_KillOnSceneChange = null;

        private string      m_PathCache     = null;
        private string      m_PathBaseValue = null;
        private Models.Actions.Misc_PlaySound m_PathModel = null;
        private AudioClip   m_AudioClip     = null;
        private AudioSource m_AudioSource   = null;
        private long        m_UIRevision    = 0;
        private long        m_AudioRevision = 0;
        private static long s_ModuleRevision = 0;

        static Misc_PlaySound()
        {
            ChatIntegrations.OnModuleDisable += OnModuleDisable;
        }

        private static void OnModuleDisable()
            => Interlocked.Increment(ref s_ModuleRevision);

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override string Description => "Play a sound clip";

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override void BuildUI(Transform p_Parent)
        {
            var l_Model      = Model;
            var l_UIRevision = ++m_UIRevision;
            var l_Choices    = new List<string>() { "<i>None</i>" };
            var l_Selected   = "<i>None</i>";
            if (!string.IsNullOrEmpty(l_Model.BaseValue) && l_Model.BaseValue != l_Selected)
            {
                l_Choices.Add(l_Model.BaseValue);
                l_Selected = l_Model.BaseValue;
            }

            XUIElements = new IXUIElement[]
            {
                Templates.SettingsHGroup("Sound clip",
                    XUIDropdown.Make()
                        .SetOptions(l_Choices).SetValue(l_Selected).SetInteractable(false)
                        .OnValueChanged((_, __) => OnSettingChanged())
                        .Bind(ref m_Dropdown)
                ),

                Templates.SettingsHGroup("Volume",
                    XUISlider.Make()
                        .SetMinValue(0.0f).SetMaxValue(1.0f).SetIncrements(0.01f).SetFormatter(CP_SDK.UI.ValueFormatters.Percentage)
                        .SetValue(Model.Volume).OnValueChanged((_) => OnSettingChanged())
                        .Bind(ref  m_Volume)
                ),

                Templates.SettingsHGroup("Pitch min/max",
                    XUISlider.Make()
                        .SetMinValue(0.0f).SetMaxValue(2.0f).SetIncrements(0.01f).SetFormatter(CP_SDK.UI.ValueFormatters.Percentage)
                        .SetValue(Model.PitchMin).OnValueChanged((_) => OnSettingChanged())
                        .Bind(ref m_PitchMin),

                    XUISlider.Make()
                        .SetMinValue(0.0f).SetMaxValue(2.0f).SetIncrements(0.01f).SetFormatter(CP_SDK.UI.ValueFormatters.Percentage)
                        .SetValue(Model.PitchMax).OnValueChanged((_) => OnSettingChanged())
                        .Bind(ref m_PitchMax)
                ),

                Templates.SettingsHGroup("Kill on scene switch?",
                    XUIToggle.Make()
                        .SetValue(Model.KillOnSceneSwitch).OnValueChanged((_) => OnSettingChanged())
                        .Bind(ref m_KillOnSceneChange)
                ),

                XUIPrimaryButton.Make("Test", OnTestButton)
            };

            BuildUIAuto(p_Parent);

            var l_Directory = Path.Combine(Environment.CurrentDirectory, ChatIntegrations.s_SOUND_CLIPS_ASSETS_PATH);
            CP_SDK.Unity.MTCoroutineStarter.Start(ApplySoundChoices(l_Directory, p_Parent, m_Dropdown, l_Model, l_UIRevision));
        }

        private bool IsCurrentSoundUI(Transform p_Parent, XUIDropdown p_Dropdown,
            Models.Actions.Misc_PlaySound p_Model, long p_UIRevision)
        {
            return p_UIRevision == m_UIRevision
                && ReferenceEquals(Model, p_Model)
                && ReferenceEquals(m_Dropdown, p_Dropdown)
                && p_Parent && p_Dropdown.Element;
        }

        private IEnumerator ApplySoundChoices(string p_Directory, Transform p_Parent,
            XUIDropdown p_Dropdown, Models.Actions.Misc_PlaySound p_Model, long p_UIRevision)
        {
            while (IsCurrentSoundUI(p_Parent, p_Dropdown, p_Model, p_UIRevision))
            {
                var l_BaseValue = p_Model.BaseValue;
                var l_Read = FileReadWorker.Enqueue(p_Directory, true, l_BaseValue);
                while (!l_Read.IsCompleted)
                    yield return null;

                var l_Result = l_Read.GetAwaiter().GetResult();
                if (!IsCurrentSoundUI(p_Parent, p_Dropdown, p_Model, p_UIRevision))
                    yield break;

                if (l_Result.Error != null)
                {
                    Logger.Instance.Error("[ChatPlexMod_ChatIntegrations.Actions][Misc_PlaySound.BuildUI] Can't list sound clips!");
                    Logger.Instance.Error(l_Result.Error);
                    p_Dropdown.SetInteractable(true);
                    yield break;
                }

                if (p_Model.BaseValue != l_BaseValue)
                    continue;

                p_Dropdown.SetOptions(l_Result.Choices, false);
                if (!IsCurrentSoundUI(p_Parent, p_Dropdown, p_Model, p_UIRevision))
                    yield break;

                if (p_Model.BaseValue != l_BaseValue)
                    continue;
                p_Dropdown.SetValue(l_Result.Selected, false);
                if (!IsCurrentSoundUI(p_Parent, p_Dropdown, p_Model, p_UIRevision))
                    yield break;
                if (p_Model.BaseValue != l_BaseValue)
                    continue;
                p_Dropdown.SetInteractable(true);
                yield break;
            }
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private void OnSettingChanged()
        {
            if (Model.BaseValue != m_Dropdown.Element.GetValue())
            {
                ++m_AudioRevision;
                m_PathCache = null;
                m_AudioClip = null;
            }

            Model.BaseValue         = m_Dropdown.Element.GetValue();
            Model.Volume            = m_Volume.Element.GetValue();
            Model.PitchMin          = m_PitchMin.Element.GetValue();
            Model.PitchMax          = m_PitchMax.Element.GetValue();
            Model.KillOnSceneSwitch = m_KillOnSceneChange.Element.GetValue();

            if (Model.BaseValue == "<i>None</i>")
                Model.BaseValue = "";
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private void OnTestButton()
        {
            CP_SDK.Unity.MTCoroutineStarter.Start(Eval(null));
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override IEnumerator Eval(EventContext p_Context)
        {
            if (Model.BaseValue != null)
            {
                if (m_PathCache == null || !ReferenceEquals(m_PathModel, Model) || m_PathBaseValue != Model.BaseValue)
                {
                    ++m_AudioRevision;
                    m_AudioClip = null;
                    m_PathModel = Model;
                    m_PathBaseValue = Model.BaseValue;
                    m_PathCache = Path.Combine(Environment.CurrentDirectory, ChatIntegrations.s_SOUND_CLIPS_ASSETS_PATH, Model.BaseValue);
                }

                yield return PlayAudioClip(m_PathCache);
            }
            else if (p_Context != null)
                p_Context.HasActionFailed = true;

            yield return null;
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private bool IsCurrentAudioRequest(string p_File, Models.Actions.Misc_PlaySound p_Model,
            string p_BaseValue, long p_AudioRevision, long p_ModuleRevision)
        {
            return ReferenceEquals(Model, p_Model) && p_Model.BaseValue == p_BaseValue
                && m_PathCache == p_File && m_AudioRevision == p_AudioRevision
                && Interlocked.Read(ref s_ModuleRevision) == p_ModuleRevision;
        }

        private IEnumerator PlayAudioClip(string p_File)
        {
            var l_Model          = Model;
            var l_BaseValue      = l_Model.BaseValue;
            var l_AudioRevision  = m_AudioRevision;
            var l_ModuleRevision = Interlocked.Read(ref s_ModuleRevision);

            if (m_AudioClip == null)
            {
                var l_Read = FileReadWorker.Enqueue(p_File, false);
                while (!l_Read.IsCompleted)
                    yield return null;

                var l_Result = l_Read.GetAwaiter().GetResult();
                if (!IsCurrentAudioRequest(p_File, l_Model, l_BaseValue, l_AudioRevision, l_ModuleRevision))
                    yield break;

                if (l_Result.Error != null)
                {
                    Logger.Instance.Error("[ChatPlexMod_ChatIntegrations.Actions][Misc_PlaySound.PlayAudioClip] Can't read audio file!");
                    Logger.Instance.Error(l_Result.Error);
                    yield break;
                }

                if (m_AudioClip == null && l_Result.Exists)
                {
                    UnityWebRequest l_Song = UnityWebRequestMultimedia.GetAudioClip(p_File, AudioType.OGGVORBIS);
                    yield return l_Song.SendWebRequest();

                    AudioClip l_Clip = null;
                    try
                    {
                        ((DownloadHandlerAudioClip)l_Song.downloadHandler).streamAudio = true;
                        l_Clip = DownloadHandlerAudioClip.GetContent(l_Song);

                        if (l_Clip == null)
                        {
                            Logger.Instance.Debug("[ChatPlexMod_ChatIntegrations.Actions][Misc_PlaySound.PlayAudioClip] No audio found!");
                            yield break;
                        }
                    }
                    catch (Exception p_Exception)
                    {
                        Logger.Instance.Error("[ChatPlexMod_ChatIntegrations.Actions][Misc_PlaySound.PlayAudioClip] Can't load audio! Exception: ");
                        Logger.Instance.Error(p_Exception);
                        yield break;
                    }

                    yield return new WaitUntil(() => !IsCurrentAudioRequest(p_File, l_Model, l_BaseValue, l_AudioRevision, l_ModuleRevision) || l_Clip);
                    if (!IsCurrentAudioRequest(p_File, l_Model, l_BaseValue, l_AudioRevision, l_ModuleRevision))
                    {
                        if (l_Clip)
                            UnityEngine.Object.Destroy(l_Clip);
                        yield break;
                    }

                    m_AudioClip = l_Clip;
                }
            }

            if (!IsCurrentAudioRequest(p_File, l_Model, l_BaseValue, l_AudioRevision, l_ModuleRevision))
                yield break;

            if (m_AudioClip != null)
            {
                if (m_AudioSource == null || !m_AudioSource)
                {
                    m_AudioSource                       = new GameObject("BSP_CI_Misc_PlaySound").AddComponent<AudioSource>();
                    m_AudioSource.loop                  = false;
                    m_AudioSource.spatialize            = false;
                    m_AudioSource.playOnAwake           = false;
                    m_AudioSource.ignoreListenerPause   = true;

                    if (!Model.KillOnSceneSwitch)
                        GameObject.DontDestroyOnLoad(m_AudioSource);
                }

                m_AudioSource.clip          = m_AudioClip;
                m_AudioSource.volume        = Model.Volume;
                m_AudioSource.pitch         = UnityEngine.Random.Range(Model.PitchMin, Model.PitchMax);
                m_AudioSource.Play();
            }
        }

        private sealed class FileReadResult
        {
            internal readonly List<string> Choices;
            internal readonly string Selected;
            internal readonly bool Exists;
            internal readonly Exception Error;

            internal FileReadResult(List<string> p_Choices, string p_Selected, bool p_Exists, Exception p_Error)
            {
                Choices = p_Choices;
                Selected = p_Selected;
                Exists = p_Exists;
                Error = p_Error;
            }
        }

        private static class FileReadWorker
        {
            private sealed class Request
            {
                internal readonly string Path;
                internal readonly bool Enumerate;
                internal readonly string SelectedFile;
                internal readonly TaskCompletionSource<FileReadResult> Completion =
                    new TaskCompletionSource<FileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);

                internal Request(string p_Path, bool p_Enumerate, string p_SelectedFile)
                {
                    Path = p_Path;
                    Enumerate = p_Enumerate;
                    SelectedFile = p_SelectedFile;
                }
            }

            private static readonly object s_Gate = new object();
            private static readonly Queue<Request> s_Pending = new Queue<Request>();
            private static Request s_Current;
            private static Task s_PhysicalTask;

            internal static Task<FileReadResult> Enqueue(string p_Path, bool p_Enumerate, string p_SelectedFile = null)
            {
                var l_Request = new Request(p_Path, p_Enumerate, p_SelectedFile);
                lock (s_Gate)
                {
                    s_Pending.Enqueue(l_Request);
                    StartIfNeeded();
                }
                return l_Request.Completion.Task;
            }

            private static void StartIfNeeded()
            {
                if (s_PhysicalTask != null || s_Pending.Count == 0)
                    return;

                s_PhysicalTask = Task.Run((System.Action)Process);
                s_PhysicalTask.ContinueWith(OnCompleted, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            private static void Process()
            {
                while (true)
                {
                    Request l_Request;
                    lock (s_Gate)
                    {
                        if (s_Pending.Count == 0)
                            return;
                        s_Current = l_Request = s_Pending.Dequeue();
                    }

                    try
                    {
                        if (l_Request.Enumerate)
                        {
                            var l_Choices = new List<string>() { "<i>None</i>" };
                            var l_Selected = "<i>None</i>";
                            foreach (var l_File in Directory.GetFiles(l_Request.Path, "*.ogg"))
                            {
                                var l_Name = System.IO.Path.GetFileName(l_File);
                                l_Choices.Add(l_Name);
                                if (l_Name == l_Request.SelectedFile)
                                    l_Selected = l_Name;
                            }
                            l_Request.Completion.TrySetResult(new FileReadResult(l_Choices, l_Selected, false, null));
                        }
                        else
                            l_Request.Completion.TrySetResult(new FileReadResult(null, null, File.Exists(l_Request.Path), null));
                    }
                    catch (Exception l_Exception)
                    {
                        l_Request.Completion.TrySetResult(new FileReadResult(null, null, false, l_Exception));
                    }

                    lock (s_Gate)
                        s_Current = null;
                }
            }

            private static void OnCompleted(Task p_Completed)
            {
                var l_Fault = p_Completed.Exception;
                lock (s_Gate)
                {
                    if (!ReferenceEquals(s_PhysicalTask, p_Completed))
                        return;

                    if (l_Fault != null && s_Current != null)
                        s_Current.Completion.TrySetResult(new FileReadResult(null, null, false, l_Fault));
                    s_Current = null;
                    s_PhysicalTask = null;
                    StartIfNeeded();
                }
            }
        }
    }

    ////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////

    public class Misc_WaitMenuScene
        : Interfaces.IAction<Misc_WaitMenuScene, Models.Actions.WaitMenuScene>
    {
        private XUIToggle m_PreventNextActionsFailure = null;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override string Description      => "Wait for menu scene";

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override void BuildUI(Transform p_Parent)
        {
            XUIElements = new IXUIElement[]
            {
                Templates.SettingsHGroup("Prevent next actions failure",
                    XUIToggle.Make()
                        .SetValue(Model.PreventNextActionFailure)
                        .OnValueChanged((_) => OnSettingChanged())
                        .Bind(ref m_PreventNextActionsFailure)
                ),

                XUIVLayout.Make(
                    XUIText.Make("This actions will delay next actions execution"),
                    XUIText.Make("until we reach the menu scene")
                )
                .SetBackground(true),
            };

            BuildUIAuto(p_Parent);
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private void OnSettingChanged()
        {
            Model.PreventNextActionFailure = m_PreventNextActionsFailure.Element.GetValue();
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override IEnumerator Eval(EventContext p_Context)
        {
            if (Model.PreventNextActionFailure)
                p_Context.PreventNextActionFailure = true;

            yield return new WaitUntil(() => CP_SDK.ChatPlexSDK.ActiveGenericScene == CP_SDK.EGenericScene.Menu);
        }
    }

    ////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////

    public class Misc_WaitPlayingScene
        : Interfaces.IAction<Misc_WaitPlayingScene, Models.Actions.WaitPlayingScene>
    {
        private XUIToggle m_PreventNextActionsFailure = null;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override string Description      => "Wait for playing scene";
        public override string UIPlaceHolder    => "Wait for playing scene";

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override void BuildUI(Transform p_Parent)
        {
            XUIElements = new IXUIElement[]
            {
                Templates.SettingsHGroup("Prevent next actions failure",
                    XUIToggle.Make()
                        .SetValue(Model.PreventNextActionFailure)
                        .OnValueChanged((_) => OnSettingChanged())
                        .Bind(ref m_PreventNextActionsFailure)
                ),

                XUIVLayout.Make(
                    XUIText.Make("This actions will delay next actions execution"),
                    XUIText.Make("until we reach the playing scene")
                )
                .SetBackground(true),
            };

            BuildUIAuto(p_Parent);
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private void OnSettingChanged()
        {
            Model.PreventNextActionFailure = m_PreventNextActionsFailure.Element.GetValue();
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override IEnumerator Eval(EventContext p_Context)
        {
            if (Model.PreventNextActionFailure)
                p_Context.PreventNextActionFailure = true;

            yield return new WaitUntil(() => CP_SDK.ChatPlexSDK.ActiveGenericScene == CP_SDK.EGenericScene.Playing);
        }
    }
}
