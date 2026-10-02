using BeatSaberPlus_MenuMusic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;

namespace ChatPlexMod_MenuMusic.Data
{
    /// <summary>
    /// Game music provider
    /// </summary>
    public class GameMusicProvider : IMusicProvider
    {
        private List<Music> m_Musics    = new List<Music>();
        private bool        m_IsLoading = false;

        private static Task s_LastLoadPublication;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override MusicProviderType.E Type                => MusicProviderType.E.GameMusic;
        public override bool                IsReady             => !m_IsLoading;
#if BEATSABER
        public override bool                SupportPlayIt       => true;
        public override bool                SupportAddToQueue   => true;
#else
#error Missing game implementation
#endif
        public override List<Music>         Musics              => m_Musics;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Constructor
        /// </summary>
        public GameMusicProvider()
        {
            m_IsLoading = true;
            CP_SDK.Unity.MTCoroutineStarter.Start(Coroutine_LoadGameSongs());
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Per game implementation of the Play It button
        /// </summary>
        /// <param name="music">Target music</param>
        /// <param name="viewController">Source view controller</param>
        public override void StartGameSpecificGamePlay(Music music, CP_SDK.UI.IViewController viewController)
        {
#if BEATSABER
            var l_BeatmapLevel = null as BeatmapLevel;
            try
            {
                if (music != null)
                {
                    var l_FullPath = music.GetSongPath();
                    if (l_FullPath.Contains("Beat Saber_Data/CustomLevels/"))
                    {
                        var l_RelativeFolder = l_FullPath.Substring(l_FullPath.IndexOf("Beat Saber_Data/CustomLevels/") + "Beat Saber_Data/CustomLevels/".Length);
                        if (l_RelativeFolder.Contains("/"))
                            l_RelativeFolder = l_RelativeFolder.Substring(0, l_RelativeFolder.LastIndexOf("/"));

                        l_BeatmapLevel = SongCore.Loader.CustomLevels.Where(x => x.Key.Contains(l_RelativeFolder)).Select(x => x.Value).FirstOrDefault();
                    }
                }
            }
            catch (System.Exception)
            {

            }

            if (l_BeatmapLevel == null)
            {
                CP_SDK.Unity.MTMainThreadInvoker.Enqueue(() =>
                {
                    if (viewController)
                        viewController.ShowMessageModal("Song not found!");
                });

                return;
            }

            CP_SDK_BS.Game.LevelSelection.FilterToSpecificSong(l_BeatmapLevel);

            return;
#else
#error Missing game implementation
#endif
        }
        /// <summary>
        /// Per game implementation of the Add to queue button
        /// </summary>
        /// <param name="music">Target music</param>
        /// <param name="viewController">Source view controller</param>
        public override void AddToQueue(Music music, CP_SDK.UI.IViewController viewController)
        {
#if BEATSABER
            if (!ModulePresence.ChatRequest)
            {
                CP_SDK.Unity.MTMainThreadInvoker.Enqueue(() =>
                {
                    if (viewController)
                        viewController.ShowMessageModal("Chat request is not installed or enabled!");
                });
                return;
            }

            var l_BeatmapLevel = null as BeatmapLevel;
            try
            {
                if (music != null)
                {
                    var l_FullPath = music.GetSongPath();
                    if (l_FullPath.Contains("Beat Saber_Data/CustomLevels/"))
                    {
                        var l_RelativeFolder = l_FullPath.Substring(l_FullPath.IndexOf("Beat Saber_Data/CustomLevels/") + "Beat Saber_Data/CustomLevels/".Length);
                        if (l_RelativeFolder.Contains("/"))
                            l_RelativeFolder = l_RelativeFolder.Substring(0, l_RelativeFolder.LastIndexOf("/"));

                        l_BeatmapLevel = SongCore.Loader.CustomLevels.Where(x => x.Key.Contains(l_RelativeFolder)).Select(x => x.Value).FirstOrDefault();
                    }
                }
            }
            catch (System.Exception)
            {

            }

            if (l_BeatmapLevel == null)
            {
                CP_SDK.Unity.MTMainThreadInvoker.Enqueue(() =>
                {
                    if (viewController)
                        viewController.ShowMessageModal("Song not found!");
                });

                return;
            }

            AddToChatRequestQueue(l_BeatmapLevel, viewController);
#else
#error Missing game implementation
#endif
        }
#if BEATSABER
        private bool AddToChatRequestQueue(BeatmapLevel beatmapLevel, CP_SDK.UI.IViewController viewController)
        {
            CP_SDK.Unity.MTMainThreadInvoker.Enqueue(() =>
            {
                if (viewController)
                    viewController.ShowLoadingModal();
            });

            BeatSaberPlus_ChatRequest.ChatRequest.Instance.AddToQueueFromBeatmapLevel(
                beatmapLevel:   beatmapLevel,
                requester:      null,
                onBehalfOf:     "$MenuMusic",
                forceNamePrefix:"🎵",
                asModAdd:       true,
                addToTop:       false,
                callback:       (p_Result) =>
                {
                    CP_SDK.Unity.MTMainThreadInvoker.Enqueue(() =>
                    {
                        if (!viewController)
                            return;

                        viewController.CloseLoadingModal();

                        switch (p_Result.Result)
                        {
                            case BeatSaberPlus_ChatRequest.Models.EAddToQueueResult.OK:
                                viewController.ShowMessageModal("Song added to the queue!");
                                break;

                            case BeatSaberPlus_ChatRequest.Models.EAddToQueueResult.NotFound:
                                viewController.ShowMessageModal("Song not found on BeatSaver!");
                                break;

                            case BeatSaberPlus_ChatRequest.Models.EAddToQueueResult.AlreadyInQueue:
                                viewController.ShowMessageModal("Song is already in queue!");
                                break;

                            default:
                                viewController.ShowMessageModal($"Error: {p_Result.Result.ToString()}");
                                break;
                        }
                    });
                }
            );

            return true;
        }
#endif
        /// <summary>
        /// Shuffle music collection
        /// </summary>
        public override void Shuffle()
        {
            for (var l_I = 0; l_I < m_Musics.Count; ++l_I)
            {
                var l_Swapped = m_Musics[l_I];
                var l_NewIndex = UnityEngine.Random.Range(l_I, m_Musics.Count);

                m_Musics[l_I] = m_Musics[l_NewIndex];
                m_Musics[l_NewIndex] = l_Swapped;
            }
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Load game songs
        /// </summary>
        /// <returns></returns>
        private IEnumerator Coroutine_LoadGameSongs()
        {
#if BEATSABER
            yield return new WaitUntil(() => !SongCore.Loader.AreSongsLoading && SongCore.Loader.CustomLevels.Count > 0);

            var l_PreviousPublication = s_LastLoadPublication;
            var l_Publication = new TaskCompletionSource<bool>();
            s_LastLoadPublication = l_Publication.Task;

            var l_Culture = CultureInfo.CurrentCulture;
            if (l_Culture.GetType() != typeof(CultureInfo) || l_Culture.TextInfo.GetType() != typeof(TextInfo))
            {
                while (l_PreviousPublication != null && !l_PreviousPublication.IsCompleted)
                    yield return null;

                try
                {
                    LoadGameSongsOnOwner();
                    Shuffle();
                    m_IsLoading = false;
                }
                finally
                {
                    CompletePublication(l_Publication);
                }
                yield break;
            }

            var l_Rows = new List<RawMusicRow>();
            Exception l_CaptureError = null;
            try
            {
                foreach (var l_Current in SongCore.Loader.CustomLevels)
                {
                    if (!(l_Current.Value.previewMediaData is FileSystemPreviewMediaData l_Preview))
                        continue;

                    l_Rows.Add(new RawMusicRow(l_Preview._previewAudioClipPath, l_Preview._coverSpritePath,
                        l_Current.Value.songName, l_Current.Value.songAuthorName));
                }
            }
            catch (Exception p_Exception)
            {
                l_CaptureError = p_Exception;
            }

            PreparationRequest l_Request = null;
            Exception l_SetupError = null;
            try
            {
                var l_TextInfo = TextInfo.ReadOnly((TextInfo)l_Culture.TextInfo.Clone());
                l_Request = new PreparationRequest(l_Rows.ToArray(), l_TextInfo);
            }
            catch (Exception p_Exception)
            {
                l_SetupError = p_Exception;
            }

            // Keep physical preparation and owner publication in admission order.
            while (l_PreviousPublication != null && !l_PreviousPublication.IsCompleted)
                yield return null;

            Task l_Preparation = null;
            if (l_SetupError == null)
            {
                try
                {
                    l_Preparation = Task.Run(l_Request.Run);
                }
                catch (Exception p_Exception)
                {
                    l_SetupError = p_Exception;
                }
            }

            while (l_Preparation != null && !l_Preparation.IsCompleted)
                yield return null;

            try
            {
                if (l_SetupError != null)
                    throw l_SetupError;
                if (l_Preparation.IsFaulted)
                    throw l_Preparation.Exception.GetBaseException();

                foreach (var l_Row in l_Request.Result.Rows)
                    m_Musics.Add(Music.FromPrepared(this, l_Row.SongPath, l_Row.CoverPath, l_Row.SongName, l_Row.SongArtist));

                if (l_Request.Result.Error != null)
                    throw l_Request.Result.Error;
                if (l_CaptureError != null)
                    throw l_CaptureError;

                Shuffle();
                m_IsLoading = false;
            }
            finally
            {
                CompletePublication(l_Publication);
            }
#else
#error Missing game implementation
#endif
        }

        private static void CompletePublication(TaskCompletionSource<bool> p_Publication)
        {
            p_Publication.SetResult(true);
            if (ReferenceEquals(s_LastLoadPublication, p_Publication.Task))
                s_LastLoadPublication = null;
        }

#if BEATSABER
        private void LoadGameSongsOnOwner()
        {
            foreach (var l_Current in SongCore.Loader.CustomLevels)
            {
                if (!(l_Current.Value.previewMediaData is FileSystemPreviewMediaData l_Preview))
                    continue;

                var l_Extension = Path.GetExtension(l_Preview._previewAudioClipPath).ToLower();
                if (l_Extension != ".egg" && l_Extension != ".ogg")
                    continue;

                m_Musics.Add(new Music(this, l_Preview._previewAudioClipPath, l_Preview._coverSpritePath,
                    l_Current.Value.songName, l_Current.Value.songAuthorName));
            }
        }
#endif

        private sealed class RawMusicRow
        {
            public readonly string SongPath;
            public readonly string CoverPath;
            public readonly string SongName;
            public readonly string SongArtist;

            public RawMusicRow(string p_SongPath, string p_CoverPath, string p_SongName, string p_SongArtist)
            {
                SongPath = p_SongPath;
                CoverPath = p_CoverPath;
                SongName = p_SongName;
                SongArtist = p_SongArtist;
            }
        }

        private sealed class PreparedMusicRow
        {
            public readonly string SongPath;
            public readonly string CoverPath;
            public readonly string SongName;
            public readonly string SongArtist;

            public PreparedMusicRow(RawMusicRow p_Row)
            {
                SongPath = p_Row.SongPath.Replace('\\', '/');
                CoverPath = p_Row.CoverPath?.Replace('\\', '/');
                SongName = p_Row.SongName.Trim();
                SongArtist = p_Row.SongArtist.Trim();
            }
        }

        private sealed class PreparationResult
        {
            public readonly List<PreparedMusicRow> Rows = new List<PreparedMusicRow>();
            public Exception Error;
        }

        private sealed class PreparationRequest
        {
            private readonly RawMusicRow[] m_Rows;
            private readonly TextInfo m_TextInfo;
            public readonly PreparationResult Result = new PreparationResult();

            public PreparationRequest(RawMusicRow[] p_Rows, TextInfo p_TextInfo)
            {
                m_Rows = p_Rows;
                m_TextInfo = p_TextInfo;
            }

            public void Run()
            {
                try
                {
                    foreach (var l_Row in m_Rows)
                    {
                        var l_Extension = Path.GetExtension(l_Row.SongPath);
                        if (l_Extension.Length == 0)
                            continue;
                        l_Extension = m_TextInfo.ToLower(l_Extension);
                        if (l_Extension != ".egg" && l_Extension != ".ogg")
                            continue;

                        Result.Rows.Add(new PreparedMusicRow(l_Row));
                    }
                }
                catch (Exception p_Exception)
                {
                    Result.Error = p_Exception;
                }
            }
        }
    }
}
