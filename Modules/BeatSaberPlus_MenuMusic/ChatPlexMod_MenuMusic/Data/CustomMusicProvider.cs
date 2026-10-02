using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ChatPlexMod_MenuMusic.Data
{
    /// <summary>
    /// Custom music provider
    /// </summary>
    public class CustomMusicProvider : IMusicProvider
    {
        private List<Music> m_Musics    = new List<Music>();
        private bool        m_IsLoading = false;

        private static Task s_LastLoadPublication;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override MusicProviderType.E Type                => MusicProviderType.E.CustomMusic;
        public override bool                IsReady             => !m_IsLoading;
        public override bool                SupportPlayIt       => false;
        public override bool                SupportAddToQueue   => false;
        public override List<Music>         Musics              => m_Musics;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Constructor
        /// </summary>
        public CustomMusicProvider()
        {
            m_IsLoading = true;
            CP_SDK.Unity.MTCoroutineStarter.Start(Coroutine_Load());
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

        }
        /// <summary>
        /// Per game implementation of the Add to queue button
        /// </summary>
        /// <param name="music">Target music</param>
        /// <param name="viewController">Source view controller</param>
        public override void AddToQueue(Music music, CP_SDK.UI.IViewController viewController)
        {

        }
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
        private IEnumerator Coroutine_Load()
        {
            yield return null;

            var l_PreviousPublication = s_LastLoadPublication;
            var l_Publication = new TaskCompletionSource<bool>();
            s_LastLoadPublication = l_Publication.Task;

            ScanRequest l_Request = null;
            Exception l_SetupError = null;
            try
            {
                var l_BaseDirectory = $"UserData/{CP_SDK.ChatPlexSDK.ProductName}Plus/MenuMusic/CustomMusic";
                l_Request = new ScanRequest(Path.GetFullPath(l_BaseDirectory));
            }
            catch (Exception p_Exception)
            {
                l_SetupError = p_Exception;
            }

            // Each admitted catalog publishes before the next scan starts.
            while (l_PreviousPublication != null && !l_PreviousPublication.IsCompleted)
                yield return null;

            Task l_Scan = null;
            if (l_SetupError == null)
            {
                try
                {
                    l_Scan = Task.Run(l_Request.Run);
                }
                catch (Exception p_Exception)
                {
                    l_SetupError = p_Exception;
                }
            }

            while (l_Scan != null && !l_Scan.IsCompleted)
                yield return null;

            try
            {
                if (l_SetupError != null)
                    throw l_SetupError;
                if (l_Scan.IsFaulted)
                    throw l_Scan.Exception.GetBaseException();

                foreach (var l_Row in l_Request.Result.Rows)
                    m_Musics.Add(new Music(this, l_Row.SongPath, l_Row.CoverPath, l_Row.SongName, " "));

                if (l_Request.Result.Error != null)
                    throw l_Request.Result.Error;

                Shuffle();
            }
            catch (Exception p_Exception)
            {
                Logger.Instance.Error("[ChatPlexMod_MenuMusic.Data][CustomMusicProvider.Coroutine_Load] GetSongsInDirectory");
                Logger.Instance.Error(p_Exception);
            }
            finally
            {
                m_IsLoading = false;
                l_Publication.SetResult(true);
                if (ReferenceEquals(s_LastLoadPublication, l_Publication.Task))
                    s_LastLoadPublication = null;
            }
        }

        private sealed class MusicRow
        {
            public readonly string SongPath;
            public readonly string CoverPath;
            public readonly string SongName;

            public MusicRow(string p_SongPath, string p_CoverPath, string p_SongName)
            {
                SongPath = p_SongPath;
                CoverPath = p_CoverPath;
                SongName = p_SongName;
            }
        }

        private sealed class ScanResult
        {
            public readonly List<MusicRow> Rows = new List<MusicRow>();
            public Exception Error;
        }

        private sealed class ScanRequest
        {
            private readonly string m_BaseDirectory;
            public readonly ScanResult Result = new ScanResult();

            public ScanRequest(string p_BaseDirectory)
                => m_BaseDirectory = p_BaseDirectory;

            public void Run()
            {
                try
                {
                    var l_Files = new List<string>();
                    if (!Directory.Exists(m_BaseDirectory))
                        Directory.CreateDirectory(m_BaseDirectory);

                    l_Files.AddRange(Directory.GetFiles(m_BaseDirectory, "*.ogg").Union(Directory.GetFiles(m_BaseDirectory, "*.egg")));
                    foreach (var l_File in l_Files)
                    {
                        var l_FixedFile = Path.GetFullPath(l_File);
                        var l_PathWithoutExtension = Path.Combine(Path.GetDirectoryName(l_FixedFile), Path.GetFileNameWithoutExtension(l_FixedFile));
                        string l_CoverPath = null;

                        if (File.Exists(l_PathWithoutExtension + ".jpg"))
                            l_CoverPath = l_PathWithoutExtension + ".jpg";
                        else if (File.Exists(l_PathWithoutExtension + ".png"))
                            l_CoverPath = l_PathWithoutExtension + ".png";

                        Result.Rows.Add(new MusicRow(l_FixedFile, l_CoverPath, Path.GetFileNameWithoutExtension(l_FixedFile)));
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
