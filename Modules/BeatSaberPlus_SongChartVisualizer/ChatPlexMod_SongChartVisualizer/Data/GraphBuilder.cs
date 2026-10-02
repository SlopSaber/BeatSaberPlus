using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

namespace ChatPlexMod_SongChartVisualizer.Data
{
    /// <summary>
    /// Graph builder utils
    /// </summary>
    internal static class GraphBuilder
    {
        private static Graph m_SampleGraph = null;
        private static readonly object m_PreparationLock = new object();
        private static readonly object m_SampleLock = new object();
        private static PreparationRequest m_PendingPreparation;
        private static bool m_Processing;
        private static GraphLayout m_SampleLayout;

        internal sealed class GraphOperation
        {
            private readonly TaskCompletionSource<bool> m_Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Task Completion => m_Completion.Task;
            internal Graph Graph;
            internal GraphLayout Layout;
            internal Exception Error;
            internal volatile bool Retired;
            internal void Retire() => RetirePreparation(this);
            internal void Complete() => m_Completion.TrySetResult(true);
        }

        internal struct GraphSegment
        {
            internal Vector2 Size;
            internal Vector2 Position;
            internal float Angle;
        }

        internal sealed class GraphLayout
        {
            internal Vector2[] Points;
            internal GraphSegment[] Segments;
            internal double[] LegendValues;
            internal bool[] LegendEnabled;
            internal string[] LegendTexts;
            internal NumberFormatInfo NumberFormat;
            internal float Width;
            internal float Height;
        }

        private sealed class PreparationRequest
        {
            internal readonly GraphOperation Operation = new GraphOperation();
            internal float[] NoteTimes;
            internal float SongDuration;
            internal float Width;
            internal float Height;
            internal int LegendCount;
            internal bool Sample;
            internal NumberFormatInfo NumberFormat;

            internal void Run()
            {
                try
                {
                    if (Sample)
                    {
                        lock (m_SampleLock)
                        {
                            Operation.Graph = GetSampleNPSGraph();
                            if (m_SampleLayout == null || m_SampleLayout.Width != Width || m_SampleLayout.Height != Height
                                || m_SampleLayout.LegendValues.Length != LegendCount || !ReferenceEquals(m_SampleLayout.NumberFormat, NumberFormat))
                                m_SampleLayout = PrepareLayout(Operation.Graph, Width, Height, LegendCount, NumberFormat);
                            Operation.Layout = m_SampleLayout;
                        }
                    }
                    else
                    {
                        var l_Raw = new List<float>((int)(SongDuration + 1f));
                        for (var l_I = 0; l_I < (int)(SongDuration + 1f); ++l_I)
                            l_Raw.Add(0f);
                        for (var l_I = 0; l_I < NoteTimes.Length; ++l_I)
                            l_Raw[(int)NoteTimes[l_I]]++;
                        Operation.Graph = new Graph(SongDuration);
                        Resample(l_Raw, Operation.Graph, new List<GraphPoint>(SongChartVisualizer.MaxPoints));
                        Operation.Layout = PrepareLayout(Operation.Graph, Width, Height, LegendCount, NumberFormat);
                    }
                }
                catch (Exception l_Exception) { Operation.Error = l_Exception; }
            }
        }

#if BEATSABER
        internal static float[] CaptureNoteTimes(IReadonlyBeatmapData p_Data, float p_SongDuration)
        {
            var l_Times = new List<float>();
            var l_BinCount = (int)(p_SongDuration + 1f);
            var l_Iterator = p_Data.allBeatmapDataItems.GetEnumerator();
            while (l_Iterator.MoveNext())
            {
                var l_Object = l_Iterator.Current;
                if (l_Object.type != BeatmapDataItem.BeatmapDataItemType.BeatmapObject
                    || !(l_Object is NoteData l_NoteData)
                    || l_NoteData.colorType == ColorType.None
                    || (int)l_NoteData.time >= (int)(p_SongDuration + 1f))
                    continue;
                var l_Time = l_NoteData.time;
                var l_Index = (int)l_Time;
                // Preserve the list index failure before advancing the live enumerator.
                if (l_Index < 0 || l_Index >= l_BinCount)
                    _ = new List<float>()[l_Index];
                l_Times.Add(l_Time);
            }
            l_Iterator.Dispose();
            return l_Times.ToArray();
        }
#endif

        internal static GraphOperation Prepare(float[] p_NoteTimes, float p_SongDuration, float p_Width, float p_Height, int p_LegendCount, NumberFormatInfo p_NumberFormat)
            => QueuePreparation(new PreparationRequest { NoteTimes = p_NoteTimes, SongDuration = p_SongDuration, Width = p_Width, Height = p_Height, LegendCount = p_LegendCount, NumberFormat = p_NumberFormat });

        internal static GraphOperation PrepareSample(float p_Width, float p_Height, int p_LegendCount, NumberFormatInfo p_NumberFormat)
            => QueuePreparation(new PreparationRequest { Sample = true, Width = p_Width, Height = p_Height, LegendCount = p_LegendCount, NumberFormat = p_NumberFormat });

        private static GraphOperation QueuePreparation(PreparationRequest p_Request)
        {
            lock (m_PreparationLock)
            {
                if (m_PendingPreparation != null)
                {
                    m_PendingPreparation.Operation.Retired = true;
                    m_PendingPreparation.Operation.Complete();
                }
                m_PendingPreparation = p_Request;
                if (!m_Processing)
                {
                    m_Processing = true;
                    try { Task.Run(ProcessPreparations); }
                    catch
                    {
                        m_Processing = false;
                        m_PendingPreparation = null;
                        throw;
                    }
                }
                return p_Request.Operation;
            }
        }

        private static void RetirePreparation(GraphOperation p_Operation)
        {
            lock (m_PreparationLock)
            {
                p_Operation.Retired = true;
                if (m_PendingPreparation != null && ReferenceEquals(m_PendingPreparation.Operation, p_Operation))
                {
                    m_PendingPreparation = null;
                    p_Operation.Complete();
                }
            }
        }

        private static void ProcessPreparations()
        {
            while (true)
            {
                PreparationRequest l_Request;
                lock (m_PreparationLock)
                {
                    l_Request = m_PendingPreparation;
                    m_PendingPreparation = null;
                    if (l_Request == null)
                    {
                        m_Processing = false;
                        return;
                    }
                }
                l_Request.Run();
                // Retiring running work must not signal completion before its buffers are ready.
                l_Request.Operation.Complete();
            }
        }

        internal static GraphLayout PrepareLayout(Graph p_Graph, float p_Width, float p_Height, int p_LegendCount, NumberFormatInfo p_NumberFormat)
        {
            var l_MinValue = p_Graph.MinY;
            var l_MaxValue = p_Graph.MaxY + 2;
            var l_YDelta = l_MaxValue - l_MinValue;
            if (l_YDelta <= 0) l_YDelta = 2.5f;
            var l_AdjustedMin = l_MinValue - (l_YDelta * 0.2f);
            l_MinValue = 0f > l_AdjustedMin ? 0f : l_AdjustedMin;
            l_MaxValue = l_MaxValue + (l_YDelta * 0.1f);
            var l_Points = p_Graph.Points == null ? null : new List<Vector2>(p_Graph.Points.Length + 1);
            var l_Segments = new List<GraphSegment>();
            if (p_Graph.Points != null)
            {
                var l_LastPoint = default(Vector2);
                var l_PointCount = p_Graph.Points.Length;
                for (var l_I = 0; l_I < l_PointCount; ++l_I)
                {
                    var l_PointX = (float)l_I * (p_Width / l_PointCount);
                    var l_PointY = ((p_Graph.Points[l_I].Y - l_MinValue) / (l_MaxValue - l_MinValue)) * p_Height;
                    var l_CurrentPoint = new Vector2(l_PointX, l_PointY);
                    l_Segments.Add(PrepareSegment(l_LastPoint, l_CurrentPoint));
                    l_Points.Add(l_CurrentPoint);
                    l_LastPoint = l_CurrentPoint;
                }
                if (l_LastPoint != default)
                {
                    var l_CurrentPoint = new Vector2(p_Width, 0f);
                    l_Segments.Add(PrepareSegment(l_LastPoint, l_CurrentPoint));
                    l_Points.Add(l_CurrentPoint);
                }
            }
            var l_Layout = new GraphLayout
            {
                Points = l_Points?.ToArray(), Segments = l_Segments.ToArray(),
                LegendValues = new double[p_LegendCount], LegendEnabled = new bool[p_LegendCount],
                LegendTexts = p_NumberFormat == null ? null : new string[p_LegendCount],
                NumberFormat = p_NumberFormat, Width = p_Width, Height = p_Height
            };
            for (var l_I = 0; l_I < p_LegendCount; ++l_I)
            {
                var l_NormalizedValue = l_I * 1f / p_LegendCount;
                var l_Value = l_MinValue + (l_NormalizedValue * (l_MaxValue - l_MinValue));
                l_Layout.LegendValues[l_I] = Math.Round(l_Value);
                l_Layout.LegendEnabled[l_I] = Math.Round(l_Value, 2) >= 0f;
                if (p_NumberFormat != null)
                    l_Layout.LegendTexts[l_I] = l_Layout.LegendValues[l_I].ToString(p_NumberFormat);
            }
            return l_Layout;
        }

        private static GraphSegment PrepareSegment(Vector2 p_Last, Vector2 p_Current)
        {
            var l_Direction = (p_Current - p_Last).normalized;
            var l_Distance = Vector2.Distance(p_Last, p_Current);
            return new GraphSegment
            {
                Size = new Vector2(l_Distance, 2f), Position = p_Last + l_Direction * l_Distance * 0.5f,
                Angle = (float)Math.Atan2(l_Direction.y, l_Direction.x) * 57.29578f
            };
        }

        private static void Resample(IList<float> p_Raw, Graph p_Graph, List<GraphPoint> p_Points)
        {
            var l_ResamplePoint = (double)p_Raw.Count / (double)SongChartVisualizer.MaxPoints;
            var l_ResampleMaxIndex = p_Raw.Count - 1;
            for (var l_I = 0; l_I < SongChartVisualizer.MaxPoints; ++l_I)
            {
                var l_VPoint = (double)l_I * l_ResamplePoint;
                var l_FirstIndex = (int)l_VPoint;
                var l_SecondIndex = l_FirstIndex >= l_ResampleMaxIndex ? l_FirstIndex : (l_FirstIndex + 1);
                var l_DeltaT = l_VPoint - l_FirstIndex;
                var l_Value = (float)(p_Raw[l_FirstIndex] * (1 - l_DeltaT) + p_Raw[l_SecondIndex] * l_DeltaT);
                p_Graph.MinY = Math.Min(p_Graph.MinY, l_Value);
                p_Graph.MaxY = Math.Max(p_Graph.MaxY, l_Value);
                p_Points.Add(new GraphPoint { X = l_I, Y = l_Value });
            }
            p_Graph.Points = p_Points.ToArray();
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

#if BEATSABER
        internal static Graph BuildNPSGraph(IReadonlyBeatmapData p_TransformedBeatmapData, float p_SongDuration)
#else
#error Missing game implementation
#endif
        {
            var l_Graph         = new Graph(p_SongDuration);
            var l_NPSSRaw       = CP_SDK.Pool.ListPool<float>.Get();
            var l_DataPoints    = CP_SDK.Pool.ListPool<GraphPoint>.Get();

            try
            {
                l_NPSSRaw.Clear();
                l_DataPoints.Clear();

                if (l_NPSSRaw.Capacity < (int)(p_SongDuration + 1))         l_NPSSRaw.Capacity          = (int)(p_SongDuration + 1);
                if (l_DataPoints.Capacity < SongChartVisualizer.MaxPoints)  l_DataPoints.Capacity       = SongChartVisualizer.MaxPoints;

                for (var l_I = 0; l_I < (int)(p_SongDuration + 1f); ++l_I)
                    l_NPSSRaw.Add(0.0f);

#if BEATSABER
                var l_Iterator = p_TransformedBeatmapData.allBeatmapDataItems.GetEnumerator();
                while (l_Iterator.MoveNext())
                {
                    var l_Object = l_Iterator.Current;
                    if (l_Object.type != BeatmapDataItem.BeatmapDataItemType.BeatmapObject
                        || !(l_Object is NoteData l_NoteData)
                        || l_NoteData.colorType == ColorType.None
                        || (int)l_NoteData.time >= (int)(p_SongDuration + 1f))
                        continue;

                    l_NPSSRaw[(int)l_NoteData.time]++;
                }
                l_Iterator.Dispose();
#else
#error Missing game implementation
#endif

                Resample(l_NPSSRaw, l_Graph, l_DataPoints);
            }
            finally
            {
                l_NPSSRaw.Clear();
                l_DataPoints.Clear();

                CP_SDK.Pool.ListPool<float>.Release(l_NPSSRaw);
                CP_SDK.Pool.ListPool<GraphPoint>.Release(l_DataPoints);
            }
#if FALSE
            var l_Builder = new System.Text.StringBuilder();
            l_Builder.AppendLine("var l_Graph = new Graph(" + p_SongDuration.ToString("0.00").Replace(',', '.') + "f)");
            l_Builder.AppendLine("{");
            l_Builder.AppendLine("    MinY   = " + l_Graph.MinY.ToString("0.00").Replace(',', '.') + "f,");
            l_Builder.AppendLine("    MaxY   = " + l_Graph.MaxY.ToString("0.00").Replace(',', '.') + "f,");
            l_Builder.AppendLine("    Points = new GraphPoint[]");
            l_Builder.AppendLine("    {");

            for (var l_I = 0; l_I < l_Graph.Points.Length; ++l_I)
                l_Builder.AppendLine("        new GraphPoint { X = " + l_Graph.Points[l_I].X.ToString("0.00").Replace(',', '.') + "f, Y = " + l_Graph.Points[l_I].Y.ToString("0.00").Replace(',', '.') + "f }" + ((l_I < (l_Graph.Points.Length - 1)) ? "," : ""));

            l_Builder.AppendLine("    }");
            l_Builder.AppendLine("};");

            System.IO.File.WriteAllText("graph.cs", l_Builder.ToString());
#endif

            return l_Graph;
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Get sample NPS graph
        /// </summary>
        /// <returns></returns>
        internal static Graph GetSampleNPSGraph()
        {
            lock (m_SampleLock)
            {
                if (m_SampleGraph != null)
                    return m_SampleGraph;

                m_SampleGraph = new Graph(225.50f)
                {
                    MinY   = 0.00f,
                    MaxY   = 13.60f,
                    Points = new GraphPoint[]
                    {
                        new GraphPoint { X =  0.00f, Y =  0.00f },
                        new GraphPoint { X =  1.00f, Y =  3.48f },
                        new GraphPoint { X =  2.00f, Y =  4.44f },
                        new GraphPoint { X =  3.00f, Y =  4.00f },
                        new GraphPoint { X =  4.00f, Y =  4.92f },
                        new GraphPoint { X =  5.00f, Y =  2.60f },
                        new GraphPoint { X =  6.00f, Y =  4.12f },
                        new GraphPoint { X =  7.00f, Y =  3.82f },
                        new GraphPoint { X =  8.00f, Y =  5.76f },
                        new GraphPoint { X =  9.00f, Y =  3.34f },
                        new GraphPoint { X = 10.00f, Y =  3.80f },
                        new GraphPoint { X = 11.00f, Y =  3.28f },
                        new GraphPoint { X = 12.00f, Y =  5.00f },
                        new GraphPoint { X = 13.00f, Y =  3.76f },
                        new GraphPoint { X = 14.00f, Y =  4.28f },
                        new GraphPoint { X = 15.00f, Y =  5.80f },
                        new GraphPoint { X = 16.00f, Y =  6.04f },
                        new GraphPoint { X = 17.00f, Y =  6.42f },
                        new GraphPoint { X = 18.00f, Y =  5.28f },
                        new GraphPoint { X = 19.00f, Y =  4.82f },
                        new GraphPoint { X = 20.00f, Y =  5.60f },
                        new GraphPoint { X = 21.00f, Y =  4.76f },
                        new GraphPoint { X = 22.00f, Y =  6.72f },
                        new GraphPoint { X = 23.00f, Y =  2.04f },
                        new GraphPoint { X = 24.00f, Y =  4.72f },
                        new GraphPoint { X = 25.00f, Y =  6.00f },
                        new GraphPoint { X = 26.00f, Y = 11.56f },
                        new GraphPoint { X = 27.00f, Y = 12.88f },
                        new GraphPoint { X = 28.00f, Y =  3.00f },
                        new GraphPoint { X = 29.00f, Y =  8.08f },
                        new GraphPoint { X = 30.00f, Y = 12.40f },
                        new GraphPoint { X = 31.00f, Y = 10.24f },
                        new GraphPoint { X = 32.00f, Y =  5.72f },
                        new GraphPoint { X = 33.00f, Y =  0.58f },
                        new GraphPoint { X = 34.00f, Y =  5.52f },
                        new GraphPoint { X = 35.00f, Y =  2.40f },
                        new GraphPoint { X = 36.00f, Y =  8.00f },
                        new GraphPoint { X = 37.00f, Y =  2.62f },
                        new GraphPoint { X = 38.00f, Y = 10.00f },
                        new GraphPoint { X = 39.00f, Y =  8.86f },
                        new GraphPoint { X = 40.00f, Y =  8.80f },
                        new GraphPoint { X = 41.00f, Y =  9.34f },
                        new GraphPoint { X = 42.00f, Y = 11.00f },
                        new GraphPoint { X = 43.00f, Y =  0.00f },
                        new GraphPoint { X = 44.00f, Y =  0.00f },
                        new GraphPoint { X = 45.00f, Y =  4.20f },
                        new GraphPoint { X = 46.00f, Y =  5.04f },
                        new GraphPoint { X = 47.00f, Y =  5.10f },
                        new GraphPoint { X = 48.00f, Y =  5.00f },
                        new GraphPoint { X = 49.00f, Y =  6.96f },
                        new GraphPoint { X = 50.00f, Y =  8.00f },
                        new GraphPoint { X = 51.00f, Y =  5.78f },
                        new GraphPoint { X = 52.00f, Y =  6.00f },
                        new GraphPoint { X = 53.00f, Y =  0.44f },
                        new GraphPoint { X = 54.00f, Y =  0.00f },
                        new GraphPoint { X = 55.00f, Y =  2.60f },
                        new GraphPoint { X = 56.00f, Y =  3.00f },
                        new GraphPoint { X = 57.00f, Y =  5.18f },
                        new GraphPoint { X = 58.00f, Y = 10.12f },
                        new GraphPoint { X = 59.00f, Y =  3.00f },
                        new GraphPoint { X = 60.00f, Y =  0.00f },
                        new GraphPoint { X = 61.00f, Y =  0.86f },
                        new GraphPoint { X = 62.00f, Y =  3.88f },
                        new GraphPoint { X = 63.00f, Y =  3.38f },
                        new GraphPoint { X = 64.00f, Y =  3.00f },
                        new GraphPoint { X = 65.00f, Y =  3.10f },
                        new GraphPoint { X = 66.00f, Y = 10.36f },
                        new GraphPoint { X = 67.00f, Y = 10.42f },
                        new GraphPoint { X = 68.00f, Y = 11.04f },
                        new GraphPoint { X = 69.00f, Y =  7.12f },
                        new GraphPoint { X = 70.00f, Y =  8.00f },
                        new GraphPoint { X = 71.00f, Y =  9.92f },
                        new GraphPoint { X = 72.00f, Y =  1.84f },
                        new GraphPoint { X = 73.00f, Y =  1.02f },
                        new GraphPoint { X = 74.00f, Y =  5.44f },
                        new GraphPoint { X = 75.00f, Y =  6.00f },
                        new GraphPoint { X = 76.00f, Y =  6.00f },
                        new GraphPoint { X = 77.00f, Y =  5.10f },
                        new GraphPoint { X = 78.00f, Y =  8.72f },
                        new GraphPoint { X = 79.00f, Y =  8.92f },
                        new GraphPoint { X = 80.00f, Y =  7.40f },
                        new GraphPoint { X = 81.00f, Y = 12.64f },
                        new GraphPoint { X = 82.00f, Y =  9.04f },
                        new GraphPoint { X = 83.00f, Y =  7.90f },
                        new GraphPoint { X = 84.00f, Y = 13.36f },
                        new GraphPoint { X = 85.00f, Y = 13.00f },
                        new GraphPoint { X = 86.00f, Y =  8.32f },
                        new GraphPoint { X = 87.00f, Y =  0.00f },
                        new GraphPoint { X = 88.00f, Y =  0.00f },
                        new GraphPoint { X = 89.00f, Y = 13.00f },
                        new GraphPoint { X = 90.00f, Y = 13.60f },
                        new GraphPoint { X = 91.00f, Y = 12.34f },
                        new GraphPoint { X = 92.00f, Y = 12.92f },
                        new GraphPoint { X = 93.00f, Y = 13.00f },
                        new GraphPoint { X = 94.00f, Y = 11.80f },
                        new GraphPoint { X = 95.00f, Y = 11.90f },
                        new GraphPoint { X = 96.00f, Y = 13.00f },
                        new GraphPoint { X = 97.00f, Y = 13.00f },
                        new GraphPoint { X = 98.00f, Y =  4.68f },
                        new GraphPoint { X = 99.00f, Y =  0.00f }
                    }
                };

                return m_SampleGraph;
            }
        }


        /*
                                     if (l_Notes.Count > 0)
                            {
                                var maximum_tolerance           = .06 + 1e-9;// # Magic number based on maximum tolerated swing speed
                                var maximum_window_tolerance    = .07 + 1e-9; //# For windowed sliders

                                NoteData l_LastRed = null;
                                NoteData l_LastBlue = null;

                                Dictionary<int, int> l_SPSR = new Dictionary<int, int>();
                                Dictionary<int, int> l_SPSB = new Dictionary<int, int>();
                                for (int l_I = 0; l_I < (l_SongDuration / l_PreviewBeatmapLevel.beatsPerMinute * 60f) + 1; ++l_I)
                                {
                                    l_SPSR.Add(l_I, 0);
                                    l_SPSB.Add(l_I, 0);
                                }

                                foreach (var l_Note in l_Notes)
                                {
                                    var real_time = l_Note.time / l_PreviewBeatmapLevel.beatsPerMinute * 60f;

                                    if (l_Note.colorType == ColorType.ColorA)
                                    {
                                        if (l_LastRed != null)
                                        {
                                            bool l_IsWindow = Mathf.Max(Mathf.Abs(l_Note.lineIndex - l_LastRed.lineIndex), Mathf.Abs(l_Note.noteLineLayer - l_LastRed.noteLineLayer)) >= 2;
                                            if (l_IsWindow && (((l_Note.time - l_LastRed.time) / l_PreviewBeatmapLevel.beatsPerMinute * 60f) > maximum_window_tolerance)
                                                || (((l_Note.time - l_LastRed.time) / l_PreviewBeatmapLevel.beatsPerMinute * 60f) > maximum_tolerance)
                                                )
                                                l_SPSR[(int)Mathf.Floor(real_time)]++;
                                        }
                                        else
                                            l_SPSR[(int)Mathf.Floor(real_time)]++;

                                        l_LastRed = l_Note;
                                    }
                                    else if (l_Note.colorType == ColorType.ColorB)
                                    {

                                        if (l_LastBlue != null)
                                        {
                                            bool l_IsWindow = Mathf.Max(Mathf.Abs(l_Note.lineIndex - l_LastBlue.lineIndex), Mathf.Abs(l_Note.noteLineLayer - l_LastBlue.noteLineLayer)) >= 2;
                                            if (l_IsWindow && (((l_Note.time - l_LastBlue.time) / l_PreviewBeatmapLevel.beatsPerMinute * 60f) > maximum_window_tolerance)
                                                || (((l_Note.time - l_LastBlue.time) / l_PreviewBeatmapLevel.beatsPerMinute * 60f) > maximum_tolerance)
                                                )
                                                l_SPSB[(int)Mathf.Floor(real_time)]++;
                                        }
                                        else
                                            l_SPSB[(int)Mathf.Floor(real_time)]++;

                                        l_LastBlue = l_Note;
                                    }
                                }

                                var swing_count_list = new Dictionary<int, int>(l_SPSR);
                                foreach (var l_KVP in l_SPSB)
                                {
                                    if (!swing_count_list.ContainsKey(l_KVP.Key))
                                        swing_count_list.Add(l_KVP.Key, l_KVP.Value);
                                    else
                                        swing_count_list[l_KVP.Key] += l_KVP.Value;
                                }

                                for (int l_I = 0; l_I < swing_count_list.Count; ++l_I)
                                {
                                    float l_StartTime = (l_I / 60f) * l_PreviewBeatmapLevel.beatsPerMinute;
                                    float l_SectionEndTime = (((l_I == swing_count_list.Count - 1) ? (l_SongDuration / l_PreviewBeatmapLevel.beatsPerMinute * 60f) : l_I + 1) / 60f) * l_PreviewBeatmapLevel.beatsPerMinute;
                                    l_GraphData.Add((swing_count_list[l_I], l_StartTime, l_SectionEndTime));
                                }


                                // float l_PrevSPS         = 0f;
                                // float l_Threshold       = 0.1f;
                                // float l_SectionStart    = 0f;
                                // for (int l_I = 0; l_I < swing_count_list.Max(x => x.Key); ++l_I)
                                // {
                                //     float l_CurrentSPS = ((float)swing_count_list[l_I]) / ((float)l_I - l_SectionStart);
                                //
                                //     if (l_I == (swing_count_list.Count - 1)
                                //         || (l_GraphData.Count == 0 && l_CurrentSPS != 0f)
                                //         || (((float)l_I) - l_SectionStart > 1f && Mathf.Abs(l_CurrentSPS - l_PrevSPS) > (l_PrevSPS * l_Threshold)))
                                //     {
                                //         var l_SectionSPS = swing_count_list.Where(x => x.Key >= l_SectionStart && x.Key < (l_I + 1)).Sum(x => x.Value) / (l_I - l_SectionStart);
                                //         l_PrevSPS = l_SectionSPS;
                                //
                                //         l_GraphData.Add((l_SectionSPS, (l_SectionStart / 60f) * l_DifficultyBeatmap.level.beatsPerMinute, (((float)l_I) / 60f) * l_DifficultyBeatmap.level.beatsPerMinute));
                                //         l_SectionStart = (float)l_I;
                                //     }
                                // }

                                swing_count_list.ToList().ForEach(x => Logger.Instance.Debug(string.Format("{0} - {1}", x.Key, x.Value)));

        */

    }
}
