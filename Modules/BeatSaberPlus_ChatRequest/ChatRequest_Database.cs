using Newtonsoft.Json.Linq;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace BeatSaberPlus_ChatRequest
{
    /// <summary>
    /// Chat request database handler
    /// </summary>
    public partial class ChatRequest
    {
        /// <summary>
        /// DB File path
        /// </summary>
        private string m_DBFilePath = System.IO.Directory.GetCurrentDirectory() + "\\UserData\\BeatSaberPlus\\ChatRequest\\Database.json";
        /// <summary>
        /// Simple queue File path
        /// </summary>
        private string m_SimpleQueueFilePath = System.IO.Directory.GetCurrentDirectory() + "\\UserData\\BeatSaberPlus\\ChatRequest\\SimpleQueue.txt";
        /// <summary>
        /// Simple queue status File path
        /// </summary>
        private string m_SimpleQueueStatusFilePath = System.IO.Directory.GetCurrentDirectory() + "\\UserData\\BeatSaberPlus\\ChatRequest\\SimpleQueueStatus.txt";

        private readonly SimpleQueueWriter m_SimpleQueueWriter = new SimpleQueueWriter();

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Load database
        /// </summary>
        private void LoadDatabase()
        {
            try
            {
                string l_FilePath = m_DBFilePath;
                if (!System.IO.File.Exists(l_FilePath))
                {
                    SaveDatabase();
                    return;
                }

                var l_JSON = JObject.Parse(System.IO.File.ReadAllText(l_FilePath, UTF8Encoding.UTF8));
                if (l_JSON["queue"] != null && l_JSON["queue"].Type == JTokenType.Array)
                {
                    foreach (JObject l_Current in (JArray)l_JSON["queue"])
                    {
                        var l_Entry = Models.SongEntry.Deserialize(l_Current);
                        if (l_Entry == null)
                            continue;

                        SongQueue.Add(l_Entry);

                        /// Start populate
                        if (l_Entry.BeatSaver_Map.Partial)
                            l_Entry.BeatSaver_Map.Populate((x) => OnBeatmapPopulated(x, l_Entry));
                    }
                }
                if (l_JSON["history"] != null && l_JSON["history"].Type == JTokenType.Array)
                {
                    foreach (JObject l_Current in (JArray)l_JSON["history"])
                    {
                        var l_Entry = Models.SongEntry.Deserialize(l_Current);
                        if (l_Entry == null)
                            continue;

                        SongHistory.Add(l_Entry);

                        /// Start populate
                        if (l_Entry.BeatSaver_Map.Partial)
                            l_Entry.BeatSaver_Map.Populate((x) => OnBeatmapPopulated(x, l_Entry));
                    }
                }
                if (l_JSON["allowlist"] != null && l_JSON["allowlist"].Type == JTokenType.Array)
                {
                    foreach (var l_CurrentRaw in (JArray)l_JSON["allowlist"])
                    {
                        var l_Current = null as JObject;
                        if (l_CurrentRaw.Type == JTokenType.String)
                        {
                            l_Current = new JObject()
                            {
                                ["key"] = l_CurrentRaw.Value<string>() ?? "",
                                ["rqn"] = "$BS+Backport"
                            };
                        }
                        else
                            l_Current = (JObject)l_CurrentRaw;

                        var l_Entry = Models.SongEntry.Deserialize(l_Current);
                        if (l_Entry == null)
                            continue;

                        SongAllowlist.Add(l_Entry);

                        /// Start populate
                        if (l_Entry.BeatSaver_Map.Partial)
                            l_Entry.BeatSaver_Map.Populate((x) => OnBeatmapPopulated(x, l_Entry));
                    }
                }
                /** LEGACY blacklist was renamed blocklist */
                if (l_JSON["blacklist"] != null && l_JSON["blacklist"].Type == JTokenType.Array)
                {
                    foreach (JObject l_Current in (JArray)l_JSON["blacklist"])
                    {
                        var l_Entry = Models.SongEntry.Deserialize(l_Current);
                        if (l_Entry == null)
                            continue;

                        SongBlocklist.Add(l_Entry);

                        /// Start populate
                        if (l_Entry.BeatSaver_Map.Partial)
                            l_Entry.BeatSaver_Map.Populate((x) => OnBeatmapPopulated(x, l_Entry));
                    }
                }
                if (l_JSON["blocklist"] != null && l_JSON["blocklist"].Type == JTokenType.Array)
                {
                    foreach (JObject l_Current in (JArray)l_JSON["blocklist"])
                    {
                        var l_Entry = Models.SongEntry.Deserialize(l_Current);
                        if (l_Entry == null)
                            continue;

                        SongBlocklist.Add(l_Entry);

                        /// Start populate
                        if (l_Entry.BeatSaver_Map.Partial)
                            l_Entry.BeatSaver_Map.Populate((x) => OnBeatmapPopulated(x, l_Entry));
                    }
                }
                if (l_JSON["bannedusers"] != null && l_JSON["bannedusers"].Type == JTokenType.Array)
                {
                    foreach (var l_Current in (JArray)l_JSON["bannedusers"])
                        BannedUsers.Add(l_Current.Value<string>() ?? "");
                }
                if (l_JSON["bannedmappers"] != null && l_JSON["bannedmappers"].Type == JTokenType.Array)
                {
                    foreach (var l_Current in (JArray)l_JSON["bannedmappers"])
                        BannedMappers.Add(l_Current.Value<string>() ?? "");
                }
                if (l_JSON["remaps"] != null && l_JSON["remaps"].Type == JTokenType.Array)
                {
                    foreach (JObject l_Current in (JArray)l_JSON["remaps"])
                    {
                        var l_Left  = (l_Current["l"]?.Value<string>() ?? "").ToLower();
                        var l_Right = (l_Current["r"]?.Value<string>() ?? "").ToLower();

                        if (string.IsNullOrEmpty(l_Left) || string.IsNullOrEmpty(l_Right) || Remaps.ContainsKey(l_Left))
                            continue;

                        Remaps.Add(l_Left, l_Right);
                    }
                }
            }
            catch (System.Exception p_Exception)
            {
                Logger.Instance.Error("LoadDataBase");
                Logger.Instance.Error(p_Exception);
            }
        }
        /// <summary>
        /// Save database
        /// </summary>
        private void SaveDatabase()
        {
            lock (SongQueue) { lock (SongHistory) { lock (SongAllowlist) { lock (SongBlocklist) { lock (BannedUsers) { lock (Remaps) {
                try
                {
                    var l_Requests  = new JArray();
                    var l_History   = new JArray();
                    var l_Allowlist = new JArray();
                    var l_Blocklist = new JArray();

                    for (var l_I = 0; l_I < SongQueue.Count;     ++l_I) l_Requests.Add(Models.SongEntry.Serialize(SongQueue[l_I]));
                    for (var l_I = 0; l_I < SongHistory.Count;   ++l_I) l_History.Add(Models.SongEntry.Serialize(SongHistory[l_I]));
                    for (var l_I = 0; l_I < SongAllowlist.Count; ++l_I) l_Allowlist.Add(Models.SongEntry.Serialize(SongAllowlist[l_I]));
                    for (var l_I = 0; l_I < SongBlocklist.Count; ++l_I) l_Blocklist.Add(Models.SongEntry.Serialize(SongBlocklist[l_I]));

                    var l_Remaps = new JArray();
                    foreach (var l_KVP in Remaps)
                    {
                        l_Remaps.Add(new JObject()
                        {
                            ["l"] = l_KVP.Key,
                            ["r"] = l_KVP.Value
                        });
                    }

                    var l_JSON = new JObject
                    {
                        { "queue",          l_Requests                          },
                        { "history",        l_History                           },
                        { "allowlist",      l_Allowlist                         },
                        { "blocklist",      l_Blocklist                         },
                        { "bannedusers",    new JArray(BannedUsers.ToArray())   },
                        { "bannedmappers",  new JArray(BannedMappers.ToArray()) },
                        { "remaps",         l_Remaps                            },
                    };

                    string l_ResultJSON = l_JSON.ToString();
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(m_DBFilePath));
                    System.IO.File.WriteAllText(m_DBFilePath, l_ResultJSON, Encoding.UTF8);
                }
                catch (System.Exception p_Exception)
                {
                    Logger.Instance.Error("SaveDatabase");
                    Logger.Instance.Error(p_Exception);
                }
            } } } } } }
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Update simple queue file
        /// </summary>
        private void UpdateSimpleQueueFile()
        {
            try
            {
                if (!m_SimpleQueueWriter.TryGetGeneration(out var l_Generation))
                    return;

                lock (SongQueue)
                {
                    var l_Config = CRConfig.Instance;
                    var l_Overlay = l_Config.OverlayIntegration;
                    var l_Rows = new List<SimpleQueueRow>();
                    for (int l_I = 0; l_I < SongQueue.Count && l_Rows.Count < l_Overlay.SimpleQueueFileCount; ++l_I)
                    {
                        var l_Song = SongQueue[l_I];
                        if (l_Song.BeatSaver_Map != null && l_Song.BeatSaver_Map.Partial)
                            continue;

                        l_Rows.Add(new SimpleQueueRow(l_I + 1, l_Song.GetSongName(), l_Song.GetLevelAuthorName(),
                            l_Song.RequesterName, l_Song.BeatSaver_Map?.id ?? "----"));
                    }

                    var l_NumberFormat = l_Rows.Count == 0 ? NumberFormatInfo.InvariantInfo
                        : NumberFormatInfo.ReadOnly((NumberFormatInfo)CultureInfo.CurrentCulture.NumberFormat.Clone());
                    m_SimpleQueueWriter.Enqueue(new SimpleQueueSnapshot(l_Generation, l_Rows.ToArray(),
                        l_NumberFormat, l_Overlay.SimpleQueueFileFormat,
                        l_Config.QueueOpen ? l_Overlay.SimpleQueueStatusOpen : l_Overlay.SimpleQueueStatusClosed,
                        m_SimpleQueueFilePath, m_SimpleQueueStatusFilePath));
                }
            }
            catch (System.Exception p_Exception)
            {
                Logger.Instance.Error("[ChatRequest] UpdateSimpleQueueFile failed");
                Logger.Instance.Error(p_Exception);
            }
        }

        private struct SimpleQueueRow
        {
            internal readonly int Index;
            internal readonly string SongName;
            internal readonly string Mapper;
            internal readonly string Requester;
            internal readonly string Key;

            internal SimpleQueueRow(int p_Index, string p_SongName, string p_Mapper, string p_Requester, string p_Key)
            {
                Index = p_Index;
                SongName = p_SongName;
                Mapper = p_Mapper;
                Requester = p_Requester;
                Key = p_Key;
            }
        }

        private sealed class SimpleQueueSnapshot
        {
            internal readonly long Generation;
            internal readonly SimpleQueueRow[] Rows;
            internal readonly NumberFormatInfo NumberFormat;
            internal readonly string Format;
            internal readonly string Status;
            internal readonly string QueuePath;
            internal readonly string StatusPath;

            internal SimpleQueueSnapshot(long p_Generation, SimpleQueueRow[] p_Rows, NumberFormatInfo p_NumberFormat,
                string p_Format, string p_Status, string p_QueuePath, string p_StatusPath)
            {
                Generation = p_Generation;
                Rows = p_Rows;
                NumberFormat = p_NumberFormat;
                Format = p_Format;
                Status = p_Status;
                QueuePath = p_QueuePath;
                StatusPath = p_StatusPath;
            }
        }

        private sealed class SimpleQueueWriter
        {
            private readonly object m_Gate = new object();
            private bool m_Active;
            private long m_Generation;
            private SimpleQueueSnapshot m_Pending;
            private Task m_Worker;

            internal void BeginSession()
            {
                lock (m_Gate)
                {
                    ++m_Generation;
                    m_Active = true;
                    m_Pending = null;
                }
            }

            internal void EndSession()
            {
                lock (m_Gate)
                {
                    ++m_Generation;
                    m_Active = false;
                    m_Pending = null;
                }
            }

            internal bool TryGetGeneration(out long p_Generation)
            {
                lock (m_Gate)
                {
                    p_Generation = m_Generation;
                    return m_Active;
                }
            }

            internal void Enqueue(SimpleQueueSnapshot p_Snapshot)
            {
                lock (m_Gate)
                {
                    if (!m_Active || p_Snapshot.Generation != m_Generation)
                        return;

                    m_Pending = p_Snapshot;
                    StartPending();
                }
            }

            private void StartPending()
            {
                if (m_Worker != null || !m_Active || m_Pending == null)
                    return;

                m_Worker = StartWorker(this);
                m_Worker.ContinueWith(OnWorkerCompleted, TaskScheduler.Default);
            }

            private static Task StartWorker(SimpleQueueWriter p_Writer)
                => Task.Run(p_Writer.WritePending);

            private void WritePending()
            {
                while (true)
                {
                    SimpleQueueSnapshot l_Snapshot;
                    lock (m_Gate)
                    {
                        if (!m_Active || m_Pending == null)
                            return;

                        l_Snapshot = m_Pending;
                        m_Pending = null;
                    }

                    try
                    {
                        var l_Content = new StringBuilder();
                        foreach (var l_Row in l_Snapshot.Rows)
                        {
                            string l_Line = l_Snapshot.Format.Replace("%i", l_Row.Index.ToString(l_Snapshot.NumberFormat))
                                .Replace("%n", l_Row.SongName)
                                .Replace("%m", l_Row.Mapper)
                                .Replace("%r", l_Row.Requester)
                                .Replace("%k", l_Row.Key);
                            if (l_Row.Index > 1)
                                l_Content.Append('\n');
                            l_Content.Append(l_Line);
                        }

                        lock (m_Gate)
                        {
                            if (!m_Active || l_Snapshot.Generation != m_Generation)
                                continue;
                        }

                        WriteLine(l_Snapshot.QueuePath, l_Content.ToString());
                        WriteLine(l_Snapshot.StatusPath, l_Snapshot.Status);
                    }
                    catch (System.Exception p_Exception)
                    {
                        Logger.Instance.Error("[ChatRequest] UpdateSimpleQueueFile failed");
                        Logger.Instance.Error(p_Exception);
                    }
                }
            }

            private static void WriteLine(string p_Path, string p_Content)
            {
                using (var l_FileStream = new System.IO.FileStream(p_Path, System.IO.FileMode.Create,
                    System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                using (var l_StreamWriter = new System.IO.StreamWriter(l_FileStream, Encoding.UTF8))
                    l_StreamWriter.WriteLine(p_Content);
            }

            private void OnWorkerCompleted(Task p_Worker)
            {
                try
                {
                    if (p_Worker.IsFaulted)
                    {
                        Logger.Instance.Error("[ChatRequest] UpdateSimpleQueueFile failed");
                        Logger.Instance.Error(p_Worker.Exception);
                    }
                }
                finally
                {
                    lock (m_Gate)
                    {
                        // Retirement retains the slot until the physical file writer has completed.
                        if (m_Worker == p_Worker)
                        {
                            m_Worker = null;
                            StartPending();
                        }
                    }
                }
            }
        }
    }
}
