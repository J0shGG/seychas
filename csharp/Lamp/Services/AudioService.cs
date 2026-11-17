using Lamp.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Lamp.Services;

public static class AudioService
{
    static readonly object Gate = new();
    static WaveOutEvent? _out;
    static VolumeSampleProvider? _volume;
    static MixingSampleProvider? _mix;
    static ISampleProvider? _ambient;
    static CancellationTokenSource? _crackle;
    static double _level = 1;

    public static void SetVolume(double level)
    {
        lock (Gate)
        {
            _level = Math.Clamp(level, 0, 1);
            if (_volume is not null) _volume.Volume = (float)_level;
        }
    }

    public static void SetAmbient(AmbientKind kind)
    {
        lock (Gate)
        {
            EnsureGraph();
            StopAmbient();
            if (kind == AmbientKind.off) return;

            var noise = new NoiseProvider(kind);
            _ambient = noise;
            _mix!.AddMixerInput(noise);
            if (kind == AmbientKind.fire)
            {
                _crackle = new CancellationTokenSource();
                var token = _crackle.Token;
                _ = Task.Run(async () =>
                {
                    var rng = new Random();
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(1400 + rng.Next(1800), token).ContinueWith(_ => { });
                        if (token.IsCancellationRequested) break;
                        PlayTone(90 + rng.Next(140), 0.08, 0.04f);
                    }
                }, token);
            }
        }
    }

    public static void PlayChime()
    {
        lock (Gate) EnsureGraph();
        double[] notes = [523.25, 659.25, 783.99];
        for (var i = 0; i < notes.Length; i++)
        {
            var delay = i * 120;
            var freq = notes[i];
            _ = Task.Delay(delay).ContinueWith(_ => PlayTone(freq, 1.4, 0.08f));
        }
    }

    static void PlayTone(double freq, double seconds, float amplitude)
    {
        lock (Gate)
        {
            EnsureGraph();
            var sine = new SignalGenerator(44100, 1)
            {
                Gain = amplitude,
                Frequency = freq,
                Type = SignalGeneratorType.Sin,
            };
            var take = new OffsetSampleProvider(sine)
            {
                TakeSamples = Math.Max(1, (int)(44100 * seconds)),
            };
            _mix!.AddMixerInput(take);
        }
    }

    static void EnsureGraph()
    {
        if (_out is not null) return;
        _mix = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 1))
        {
            ReadFully = true,
        };
        _volume = new VolumeSampleProvider(_mix) { Volume = (float)_level };
        _out = new WaveOutEvent();
        _out.Init(_volume);
        _out.Play();
    }

    static void StopAmbient()
    {
        _crackle?.Cancel();
        _crackle = null;
        if (_ambient is not null && _mix is not null)
        {
            _mix.RemoveMixerInput(_ambient);
            _ambient = null;
        }
    }

    sealed class NoiseProvider : ISampleProvider
    {
        readonly AmbientKind _kind;
        readonly Random _rng = new();
        float _last;
        float _gain;

        public NoiseProvider(AmbientKind kind)
        {
            _kind = kind;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var target = _kind switch
            {
                AmbientKind.rain => 0.045f,
                AmbientKind.fire => 0.055f,
                _ => 0.035f,
            };
            for (var i = 0; i < count; i++)
            {
                _gain += (target - _gain) * 0.0003f;
                var white = (float)(_rng.NextDouble() * 2 - 1);
                float sample;
                if (_kind == AmbientKind.rain)
                {
                    _last = _last * 0.7f + white * 0.3f;
                    sample = _last;
                }
                else
                {
                    _last = (_last + 0.02f * white) / 1.02f;
                    sample = _last * 3.5f;
                }
                buffer[offset + i] = Math.Clamp(sample * _gain, -0.4f, 0.4f);
            }
            return count;
        }
    }
}
