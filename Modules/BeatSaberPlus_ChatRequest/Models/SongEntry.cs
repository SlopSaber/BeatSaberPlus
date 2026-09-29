using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace BeatSaberPlus_ChatRequest.Models
{
    /// <summary>
    /// Song entry
    /// </summary>
    public class SongEntry : CP_SDK_BS.UI.Data.SongListItem
    {
        private static SongEntry m_RemotePreviewSelection;
        private static AudioClip m_RemotePreviewClip;
        private static string m_RemotePreviewHash = "";
        private static int m_RemotePreviewSerial;

        internal DateTime?      RequestTime     = null;
        internal string         RequesterName   = "";
        internal string         Message         = "";

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Serialize into a JObject
        /// </summary>
        /// <param name="p_SongEntry">Source</param>
        /// <returns></returns>
        internal static JObject Serialize(SongEntry p_SongEntry)
        {
            return new JObject
            {
                ["key"] = p_SongEntry.BeatSaver_Map?.id ?? p_SongEntry.GetLevelHash(),
                ["rqt"] = p_SongEntry.RequestTime.HasValue ? CP_SDK.Misc.Time.ToUnixTime(p_SongEntry.RequestTime.Value) : CP_SDK.Misc.Time.UnixTimeNow(),
                ["rqn"] = p_SongEntry.RequesterName,
                ["npr"] = p_SongEntry.TitlePrefix,
                ["msg"] = p_SongEntry.Message
            };
        }
        /// <summary>
        /// Deserialize from JObject
        /// </summary>
        /// <param name="p_JObject">Source</param>
        /// <returns></returns>
        internal static SongEntry Deserialize(JObject p_JObject)
        {
            var l_Key       = p_JObject["key"]?.Value<string>()     ?? "";
            var l_Time      = p_JObject["rqt"]?.Value<long>()       ?? CP_SDK.Misc.Time.UnixTimeNow();
            var l_Requester = p_JObject["rqn"]?.Value<string>()     ?? "";
            var l_Prefix    = p_JObject["npr"]?.Value<string>()     ?? "";
            var l_Message   = p_JObject["msg"]?.Value<string>()     ?? "";

            if (l_Key == "" && p_JObject.ContainsKey("id"))
                l_Key = p_JObject["id"].Value<int>().ToString("x");

            if (l_Key == "")
                return null;

            return new SongEntry()
            {
                BeatSaver_Map   = CP_SDK_BS.Game.BeatMapsClient.GetFromCacheByKey(l_Key) ?? CP_SDK_BS.Game.BeatMaps.MapDetail.PartialFromKey(l_Key),
                RequestTime     = CP_SDK.Misc.Time.FromUnixTime(l_Time),
                RequesterName   = l_Requester,
                TitlePrefix     = l_Prefix,
                Message         = l_Message
            };
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On show
        /// </summary>
        public override void OnShow()
        {
            Tooltip = "<b>Requested by</b> " + TitlePrefix + (TitlePrefix.Length != 0 ? " " : "") + RequesterName;

            if (RequestTime.HasValue)
            {
                var l_Elapsed = CP_SDK.Misc.Time.UnixTimeNow() - CP_SDK.Misc.Time.ToUnixTime(RequestTime.Value);
                if (l_Elapsed < (60 * 60))
                    Tooltip += "\n<align=\"center\">" + Math.Max(1, l_Elapsed / 60).ToString() + " minute(s) ago</align>";
                else if (l_Elapsed < (60 * 60 * 24))
                    Tooltip += "\n<align=\"center\">" + Math.Max(1, l_Elapsed / (60 * 60)).ToString() + " hour(s) ago</align>";
                else
                    Tooltip += "\n<align=\"center\">" + Math.Max(1, l_Elapsed / (60 * 60 * 24)).ToString() + " day(s) ago</align>";
            }

            if (!string.IsNullOrEmpty(Message))
                Tooltip += "\n" + Message;

            base.OnShow();
        }
        /// <summary>Play BeatSaver audio when the selected request is not downloaded.</summary>
        public override void OnSelect()
        {
            StopRemotePreview();
            base.OnSelect();

            if (!(SongListController?.PlayPreviewAudio() ?? false)
                || BeatSaver_Map == null || BeatSaver_Map.Partial
                || CP_SDK_BS.Game.Levels.TryGetBeatmapLevelForHash(GetLevelHash(), out _, silentFail: true))
                return;

            var l_Hash = BeatSaver_Map.SelectMapVersion()?.hash;
            if (string.IsNullOrEmpty(l_Hash))
                return;

            m_RemotePreviewSelection = this;
            if (m_RemotePreviewHash == l_Hash && m_RemotePreviewClip)
            {
                PlayRemotePreview(m_RemotePreviewClip);
                return;
            }

            CP_SDK.Unity.MTCoroutineStarter.Start(LoadRemotePreview(l_Hash, m_RemotePreviewSerial));
        }

        public override void OnUnselect()
        {
            if (m_RemotePreviewSelection == this)
                StopRemotePreview();

            base.OnUnselect();
        }

        internal static void StopRemotePreview()
        {
            ++m_RemotePreviewSerial;
            m_RemotePreviewSelection = null;
        }

        private void PlayRemotePreview(AudioClip p_Clip)
        {
            var l_Player = Resources.FindObjectsOfTypeAll<SongPreviewPlayer>().FirstOrDefault();
            if (l_Player)
                l_Player.CrossfadeTo(p_Clip, SongListController?.PreviewAudioVolume() ?? 1f, 0f, p_Clip.length, null);
        }

        private IEnumerator LoadRemotePreview(string p_Hash, int p_Serial)
        {
            var l_URL = $"https://cdn.beatsaver.com/{p_Hash.ToLowerInvariant()}.mp3";
            using (var l_Request = UnityWebRequestMultimedia.GetAudioClip(l_URL, AudioType.MPEG))
            {
                l_Request.timeout = 15;
                var l_Operation = l_Request.SendWebRequest();
                while (!l_Operation.isDone)
                {
                    if (p_Serial != m_RemotePreviewSerial)
                    {
                        l_Request.Abort();
                        yield break;
                    }

                    yield return null;
                }

                if (p_Serial != m_RemotePreviewSerial)
                    yield break;

                if (l_Request.result != UnityWebRequest.Result.Success)
                {
                    CP_SDK.ChatPlexSDK.Logger.Error($"[ChatRequest] BeatSaver preview failed for {p_Hash}: {l_Request.error}");
                    yield break;
                }

                AudioClip l_Clip;
                try { l_Clip = DownloadHandlerAudioClip.GetContent(l_Request); }
                catch (Exception p_Exception)
                {
                    CP_SDK.ChatPlexSDK.Logger.Error("[ChatRequest] Could not decode BeatSaver preview:");
                    CP_SDK.ChatPlexSDK.Logger.Error(p_Exception);
                    yield break;
                }

                if (!l_Clip)
                    yield break;

                var l_Deadline = Time.realtimeSinceStartup + 3f;
                while (l_Clip.loadState == AudioDataLoadState.Loading && p_Serial == m_RemotePreviewSerial
                    && Time.realtimeSinceStartup < l_Deadline)
                    yield return null;

                if (p_Serial != m_RemotePreviewSerial || l_Clip.loadState != AudioDataLoadState.Loaded
                    || m_RemotePreviewSelection != this || CP_SDK.ChatPlexSDK.ActiveGenericScene != CP_SDK.EGenericScene.Menu)
                {
                    UnityEngine.Object.Destroy(l_Clip);
                    yield break;
                }

                var l_PreviousClip = m_RemotePreviewClip;
                m_RemotePreviewClip = l_Clip;
                m_RemotePreviewHash = p_Hash;
                PlayRemotePreview(l_Clip);
                if (l_PreviousClip && l_PreviousClip != l_Clip)
                    UnityEngine.Object.Destroy(l_PreviousClip, 5f);
            }
        }
    }
}
