using System;

namespace WorldSphereMod.Bridge
{
    public sealed class FrameSampleRing
    {
        public struct Sample
        {
            public int frame;
            public float intervalMs;
            public int gen0Collections, gen1Collections, gen2Collections;
            public bool focused;
            public bool? terrainDirty;
            public long? fullRebuildCount, incrementalUpdateCount;
        }
        readonly Sample[] samples;
        int next, count;
        public long TotalSamples { get; private set; }
        public FrameSampleRing(int capacity)
        {
            if (capacity < 1 || capacity > 36000) throw new ArgumentOutOfRangeException(nameof(capacity));
            samples = new Sample[capacity];
        }
        public void Add(Sample sample)
        {
            samples[next] = sample;
            next = (next + 1) % samples.Length;
            if (count < samples.Length) count++;
            TotalSamples++;
        }
        public Sample[] Snapshot()
        {
            var result = new Sample[count];
            int first = (next - count + samples.Length) % samples.Length;
            for (int i = 0; i < count; i++) result[i] = samples[(first + i) % samples.Length];
            return result;
        }
    }
}
