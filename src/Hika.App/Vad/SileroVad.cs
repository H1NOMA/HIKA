using Hika.Audio;
using Hika.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Hika.Vad;

/// <summary>
/// Silero VAD — небольшая нейросеть, отличающая речь от всего остального.
/// Два мегабайта, около миллисекунды на кадр, и качество несопоставимо
/// с любой пороговой схемой: она не считает речью ни музыку, ни клавиатуру.
///
/// У модели за версии менялась сигнатура: в четвёртой были отдельные состояния
/// h и c, в пятой — одно объединённое state. Вместо того чтобы гадать, читаем
/// метаданные модели при загрузке и подстраиваемся. Так одна и та же сборка
/// переживёт и обновление файла модели.
/// </summary>
public sealed class SileroVad : IVoiceActivityDetector
{
    private readonly InferenceSession _session;
    private readonly bool _v5;                 // true — одно состояние state, false — h/c
    private readonly bool _srIsScalar;
    private readonly string _inputName;
    private readonly string _srName;
    private readonly string _outputName;

    private float[] _state = Array.Empty<float>();   // v5: [2,1,128]
    private float[] _h = Array.Empty<float>();       // v4: [2,1,64]
    private float[] _c = Array.Empty<float>();

    /// <summary>
    /// Хвост предыдущего кадра, который пятая версия ожидает увидеть перед новым.
    ///
    /// Это самая коварная деталь во всей модели. Silero v5 принимает не 512
    /// отсчётов, а 576: последние 64 отсчёта прошлого кадра плюс новые 512.
    /// Размерность у входа плавающая, поэтому при подаче голых 512 модель
    /// не ругается — она честно считает и стабильно отвечает «речи нет».
    /// Снаружи это выглядит как полностью глухой ассистент при исправном звуке.
    /// </summary>
    private const int ContextSamples = 64;

    private readonly float[] _context;
    private readonly float[] _modelInput;
    private readonly int[] _inputDims;

    /// <summary>Наибольшая уверенность за всё время работы — для диагностики.</summary>
    public float MaxProbabilitySeen { get; private set; }

    public string Name => _v5 ? "Silero VAD v5" : "Silero VAD v4";

    public SileroVad(string modelPath)
    {
        var options = new SessionOptions
        {
            // Модель крошечная, и она работает на каждом кадре в фоне круглые сутки.
            // Один поток здесь быстрее многопоточности: накладные расходы на
            // синхронизацию больше самой работы.
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };

        _session = new InferenceSession(modelPath, options);

        var inputs = _session.InputMetadata.Keys.ToList();
        var outputs = _session.OutputMetadata.Keys.ToList();

        _v5 = inputs.Contains("state");
        _inputName = inputs.FirstOrDefault(n => n is "input") ?? inputs[0];
        _srName = inputs.FirstOrDefault(n => n is "sr") ?? "sr";
        _outputName = outputs.FirstOrDefault(n => n is "output") ?? outputs[0];

        var srDims = _session.InputMetadata.TryGetValue(_srName, out var srMeta)
            ? srMeta.Dimensions
            : new[] { 1 };
        _srIsScalar = srDims is { Length: 0 };

        // Четвёртая версия работает ровно на 512 отсчётах, пятая ждёт контекст.
        var contextSize = _v5 ? ContextSamples : 0;

        _context = new float[Math.Max(1, contextSize)];
        _modelInput = new float[contextSize + AudioFormat.FrameSamples];
        _inputDims = new[] { 1, _modelInput.Length };

        Reset();

        Log.Info($"{Name}: входы [{string.Join(", ", inputs)}], выходы [{string.Join(", ", outputs)}]", "vad");
    }

    public float Process(ReadOnlySpan<float> frame)
    {
        if (frame.Length == 0) return 0f;

        try
        {
            // Собираем вход: хвост прошлого кадра, затем новые отсчёты.
            var offset = _v5 ? ContextSamples : 0;
            if (offset > 0) Array.Copy(_context, 0, _modelInput, 0, offset);

            var n = Math.Min(frame.Length, AudioFormat.FrameSamples);
            frame[..n].CopyTo(_modelInput.AsSpan(offset));

            // Неполный кадр дополняем тишиной: и модель, и измеритель
            // рассчитывают на постоянный размер окна.
            if (n < AudioFormat.FrameSamples)
                Array.Clear(_modelInput, offset + n, AudioFormat.FrameSamples - n);

            var audio = new DenseTensor<float>(_modelInput, _inputDims);

            var sr = _srIsScalar
                ? new DenseTensor<long>(new long[] { AudioFormat.SampleRate }, Array.Empty<int>())
                : new DenseTensor<long>(new long[] { AudioFormat.SampleRate }, new[] { 1 });

            var feeds = new List<NamedOnnxValue>(4)
            {
                NamedOnnxValue.CreateFromTensor(_inputName, audio),
                NamedOnnxValue.CreateFromTensor(_srName, sr),
            };

            if (_v5)
            {
                feeds.Add(NamedOnnxValue.CreateFromTensor("state",
                    new DenseTensor<float>(_state, new[] { 2, 1, 128 })));
            }
            else
            {
                feeds.Add(NamedOnnxValue.CreateFromTensor("h", new DenseTensor<float>(_h, new[] { 2, 1, 64 })));
                feeds.Add(NamedOnnxValue.CreateFromTensor("c", new DenseTensor<float>(_c, new[] { 2, 1, 64 })));
            }

            using var results = _session.Run(feeds);

            float probability = 0f;
            foreach (var r in results)
            {
                if (r.Name == _outputName)
                {
                    var t = r.AsTensor<float>();
                    probability = t.Length > 0 ? t.GetValue(0) : 0f;
                }
                else if (_v5 && r.Name is "stateN" or "state")
                {
                    r.AsTensor<float>().ToArray().CopyTo(_state, 0);
                }
                else if (!_v5 && r.Name is "hn" or "h")
                {
                    r.AsTensor<float>().ToArray().CopyTo(_h, 0);
                }
                else if (!_v5 && r.Name is "cn" or "c")
                {
                    r.AsTensor<float>().ToArray().CopyTo(_c, 0);
                }
            }

            // Хвост текущего входа станет контекстом для следующего кадра.
            if (_v5) Array.Copy(_modelInput, _modelInput.Length - ContextSamples, _context, 0, ContextSamples);

            probability = Math.Clamp(probability, 0f, 1f);
            if (probability > MaxProbabilitySeen) MaxProbabilitySeen = probability;

            return probability;
        }
        catch (Exception ex)
        {
            // Один сбой не должен ронять пайплайн: сбрасываем состояние и живём дальше.
            Log.Warn($"кадр не прошёл через VAD: {ex.Message}", "vad");
            Reset();
            return 0f;
        }
    }

    public void Reset()
    {
        if (_v5)
        {
            _state = new float[2 * 1 * 128];
        }
        else
        {
            _h = new float[2 * 1 * 64];
            _c = new float[2 * 1 * 64];
        }

        Array.Clear(_context);
    }

    public void Dispose() => _session.Dispose();
}
