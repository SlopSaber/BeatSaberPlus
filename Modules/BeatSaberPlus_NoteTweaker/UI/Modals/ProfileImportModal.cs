using CP_SDK;
using CP_SDK.XUI;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;

namespace BeatSaberPlus_NoteTweaker.UI.Modals
{
    /// <summary>
    /// Profile import modal
    /// </summary>
    internal sealed class ProfileImportModal : CP_SDK.UI.IModal
    {
        private XUIDropdown m_Dropdown = null;

        private Action m_Callback = null;
        private string m_Selected = null;
        private object m_Session = null;
        private bool m_Busy = false;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On modal show
        /// </summary>
        public override void OnShow()
        {
            if (m_Dropdown != null)
                return;

            Templates.ModalRectLayout(
                XUIText.Make("What profile do you want to import?"),

                XUIDropdown.Make()
                    .OnValueChanged((_, p_Selected) => m_Selected = p_Selected)
                    .Bind(ref m_Dropdown),

                XUIHLayout.Make(
                    XUISecondaryButton.Make("Cancel", OnCancelButton).SetWidth(30f),
                    XUIPrimaryButton.Make("Import", OnImportButton).SetWidth(30f)
                )
                .SetPadding(0)
            )
            .SetWidth(90.0f)
            .BuildUI(transform);
        }
        /// <summary>
        /// On modal close
        /// </summary>
        public override void OnClose()
        {
            m_Session = null;
            m_Callback = null;
            m_Busy = false;
        }

        private void OnDisable() => OnClose();
        private void OnDestroy() => OnClose();

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Init
        /// </summary>
        /// <param name="p_Callback">Callback</param>
        public void Init(Action p_Callback)
        {
            m_Session = new object();
            m_Callback = p_Callback;
            m_Selected = string.Empty;
            m_Busy = true;
            m_Dropdown.SetInteractable(false).SetOptions(new List<string>());

            var l_Operation = NoteTweaker.BeginProfileScan();
            CP_SDK.Unity.MTCoroutineStarter.Start(WaitForProfiles(l_Operation, m_Session, NoteTweaker.FileSession));
        }

        private bool IsCurrent(object p_Session, object p_ModuleSession)
        {
            return this && VController && gameObject.activeInHierarchy
                && object.ReferenceEquals(m_Session, p_Session)
                && object.ReferenceEquals(NoteTweaker.FileSession, p_ModuleSession);
        }

        private IEnumerator WaitForProfiles(NoteTweaker.ProfileFileOperation p_Operation, object p_Session, object p_ModuleSession)
        {
            while (!p_Operation.Completion.IsCompleted)
                yield return null;

            var l_Result = p_Operation.Result;
            LogFileError(l_Result.Error);
            if (!IsCurrent(p_Session, p_ModuleSession))
                yield break;
            if (l_Result.Error != null)
            {
                VController.CloseModal(this);
                VController.ShowMessageModal("Error reading profiles!");
                yield break;
            }
            m_Dropdown.SetOptions(l_Result.Names).SetInteractable(true);
            m_Busy = false;
        }

        private static void LogFileError(Exception p_Error)
        {
            if (p_Error == null)
                return;
            Logger.Instance.Error("[BeatSaberPlus_NoteTweaker.UI][ProfileImportModal.ProfileFile] Error:");
            Logger.Instance.Error(p_Error);
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On cancel button
        /// </summary>
        private void OnCancelButton()
        {
            VController.CloseModal(this);
        }
        /// <summary>
        /// On import button
        /// </summary>
        private void OnImportButton()
        {
            if (m_Busy)
                return;
            m_Busy = true;
            m_Dropdown.SetInteractable(false);
            var l_Operation = NoteTweaker.BeginProfileImport(m_Selected);
            CP_SDK.Unity.MTCoroutineStarter.Start(WaitForProfileImport(l_Operation, m_Session, NoteTweaker.FileSession));
        }

        private IEnumerator WaitForProfileImport(NoteTweaker.ProfileFileOperation p_Operation, object p_Session, object p_ModuleSession)
        {
            while (!p_Operation.Completion.IsCompleted)
                yield return null;

            var l_Result = p_Operation.Result;
            LogFileError(l_Result.Error);
            if (!IsCurrent(p_Session, p_ModuleSession))
                yield break;
            if (l_Result.Error != null || l_Result.Missing || l_Result.Invalid)
            {
                VController.CloseModal(this);
                VController.ShowMessageModal(l_Result.Error != null ? "Error reading profile!"
                    : l_Result.Missing ? "File not found!" : "Invalid file!");
                yield break;
            }

            var l_NewProfile = l_Result.Profile;
            if (l_Result.ParseOnOwner)
            {
                bool l_Invalid = false;
                try
                {
                    l_NewProfile = JsonConvert.DeserializeObject<NTConfig._Profile>(l_Result.Raw,
                        new JsonConverter[] { new CP_SDK.Config.JsonConverters.ColorConverter() });
                    l_NewProfile.Name += " (Imported)";
                }
                catch { l_Invalid = true; }
                if (!IsCurrent(p_Session, p_ModuleSession))
                    yield break;
                if (l_Invalid)
                {
                    VController.CloseModal(this);
                    VController.ShowMessageModal("Invalid file!");
                    yield break;
                }
            }

            if (!IsCurrent(p_Session, p_ModuleSession))
                yield break;
            if (l_NewProfile == null)
            {
                VController.CloseModal(this);
                VController.ShowMessageModal("Error importing profile!");
                yield break;
            }

            var l_Callback = m_Callback;
            NTConfig.Instance.Profiles.Add(l_NewProfile);
            VController.CloseModal(this);
            try { l_Callback?.Invoke(); }
            catch (Exception l_Exception)
            {
                Logger.Instance.Error("[BeatSaberPlus_NoteTweaker.UI][ProfileImportModal.OnImportButton] Error:");
                Logger.Instance.Error(l_Exception);
            }
        }
    }
}
