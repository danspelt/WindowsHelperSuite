using System.Globalization;
using System.Speech.Recognition;

namespace StillSpace.Services;

public sealed class SpeechDictationService : IDisposable
{
    private SpeechRecognitionEngine? _engine;

    public bool IsAvailable
    {
        get
        {
            try
            {
                _ = CultureInfo.GetCultureInfo("en-US");
                return OperatingSystem.IsWindows();
            }
            catch
            {
                return false;
            }
        }
    }

    public void Start(
        string cultureName,
        Action<string> onHypothesis,
        Action<string> onFinal)
    {
        Stop();
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(cultureName);
        }
        catch
        {
            culture = CultureInfo.GetCultureInfo("en-US");
        }

        _engine = new SpeechRecognitionEngine(culture);
        try
        {
            _engine.SetInputToDefaultAudioDevice();
        }
        catch
        {
            /* ignore — engine may still bind to default */
        }

        _engine.LoadGrammar(new DictationGrammar());
        _engine.SpeechHypothesized += (_, e) => onHypothesis(e.Result.Text);
        _engine.SpeechRecognized += (_, e) => onFinal(e.Result.Text);
        try
        {
            _engine.RecognizeAsync(RecognizeMode.Multiple);
        }
        catch
        {
            Stop();
        }
    }

    public void Stop()
    {
        if (_engine == null) return;
        try
        {
            _engine.RecognizeAsyncStop();
        }
        catch
        {
            /* ignore */
        }

        _engine.Dispose();
        _engine = null;
    }

    public void Dispose() => Stop();
}
