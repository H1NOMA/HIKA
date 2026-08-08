using NAudio.Wave;

namespace Hika.Audio;

/// <summary>
/// Сводит любое число каналов в моно усреднением.
///
/// В NAudio есть StereoToMonoSampleProvider, но он умеет ровно два канала,
/// а на машинах с гарнитурами, вебкамерами и интерфейсами вроде Voicemeeter
/// микрофон запросто оказывается четырёх- или шестиканальным.
/// </summary>
public sealed class MonoMixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _buffer = Array.Empty<float>();

    public WaveFormat WaveFormat { get; }

    public MonoMixSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_sourceChannels == 1) return _source.Read(buffer, offset, count);

        var needed = count * _sourceChannels;
        if (_buffer.Length < needed) _buffer = new float[needed];

        var read = _source.Read(_buffer, 0, needed);
        if (read <= 0) return 0;

        var frames = read / _sourceChannels;
        var scale = 1f / _sourceChannels;

        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            var b = f * _sourceChannels;
            for (int c = 0; c < _sourceChannels; c++) sum += _buffer[b + c];
            buffer[offset + f] = sum * scale;
        }

        return frames;
    }
}
