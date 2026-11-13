using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Knockbox.Game.Online
{
    /// <summary>Separate schema: authoritative/predicted ticks and SDK RTT are not the former lockstep input-age metric.</summary>
    public sealed class FusionPredictionDiagnostics : IDisposable
    {
        StreamWriter writer;
        readonly double started;
        readonly int slot, delay;
        double nextWrite, frameSum, frameMax;
        int frames;
        public string FilePath { get; private set; }

        public FusionPredictionDiagnostics(int slot, int delay)
        {
            this.slot = slot; this.delay = delay;
            started = Time.realtimeSinceStartupAsDouble;
            nextWrite = started + 1;
            try
            {
                string folder = Path.Combine(ReplayFiles.Folder, "Network");
                Directory.CreateDirectory(folder);
                FilePath = Path.Combine(folder, $"online_prediction_P{slot + 1}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fffZ}_{Guid.NewGuid():N}.csv");
                writer = new StreamWriter(FilePath);
                writer.WriteLine("schema,elapsed_s,slot,delay_ticks,network_hz,send_hz,path,control_rtt_ms,sdk_rtt_ms,predicted_tick,confirmed_tick,forward_total,resim_total,corrected_ticks_total,max_same_tick_correction,missing_local_total,host_missing_remote_total,local_samples_total,local_age_mean_ms,local_age_max_ms,verified_batches,frame_mean_ms,frame_max_ms,snapshot_bytes,build_guid");
                writer.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Disable(e); }
        }

        public void RecordFrame(FusionPredictionSession s, double controlRtt, FusionTransport.ConnectionTiming timing)
        {
            if (writer == null) return;
            frames++; frameSum += Time.unscaledDeltaTime; frameMax = Math.Max(frameMax, Time.unscaledDeltaTime);
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextWrite) return;
            try
            {
                writer.WriteLine(string.Join(",", "prediction-v1", F(now - started), slot + 1, delay,
                    timing.TickRate, timing.SendRate, timing.Path, F(controlRtt * 1000), F(timing.RttSeconds * 1000),
                    s.Match.World.State.Tick, s.Journal.World.State.Tick, s.ForwardTicks, s.ResimulationTicks,
                    s.CorrectedTicks, F(s.LargestCorrection), s.MissingLocalInputs, s.MissingRemoteInputs,
                    s.LocalInputSamples, F(s.LocalInputSamples > 0 ? s.LocalInputAgeTotal * 1000 / s.LocalInputSamples : -1),
                    F(s.LocalInputAgeMax * 1000), s.Journal.BatchesVerified, F(frameSum * 1000 / frames),
                    F(frameMax * 1000), FusionWorldSnapshot.ByteCount, Application.buildGUID));
                writer.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Disable(e); }
            frames = 0; frameSum = frameMax = 0; nextWrite = now + 1;
        }

        static string F(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        void Disable(Exception e)
        {
            try { writer?.Dispose(); } catch (IOException) { }
            writer = null;
            Debug.LogWarning($"[Knockbox][prediction] 진단 저장 중단: {e.GetType().Name}");
        }
        public void Dispose()
        {
            try { writer?.Dispose(); } catch (IOException) { }
            writer = null;
        }
    }
}
