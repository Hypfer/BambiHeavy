// Adaptive temporal integration with transient bypass
//
// Problem: the TemporalIntegrator smooths noise but also lags during genuine
// color changes. This wrapper detects when the input is changing too fast,
// temporarily switches to fast blending, then recovers to normal smoothing.

using BambiHeavy.Models;

namespace BambiHeavy.Pipeline;

public class AdaptiveIntegrator
{
    private readonly TemporalIntegrator _inner;

    private int _bypassFrameCount;
    private double _bypassFrames;
    private bool _enabled;
    private bool _hasPrevious;
    private bool _integrationEnabled;

    private double _prevR, _prevG, _prevB;
    private int _recoveryFrame;
    private int _settleCount;
    private int _settleFrames;
    private double _settleThreshold;
    private State _state;
    private int _totalFrameCount;

    private double _transientThreshold;

    public AdaptiveIntegrator()
    {
        _inner = new TemporalIntegrator();
        _state = State.Normal;
        _transientThreshold = 0.157; // 40/255
        _settleThreshold = 0.059; // 15/255
        _settleFrames = 3;
        _bypassFrames = 2;
        _enabled = true;
        _integrationEnabled = true;
    }

    public void Apply(ref double r, ref double g, ref double b, TuningProfile profile, PipelineSettings settings)
    {
        if (!_integrationEnabled) return;

        double rIn = r, gIn = g, bIn = b;

        if (_enabled)
        {
            _totalFrameCount++;

            var dR = Math.Abs(rIn - _prevR);
            var dG = Math.Abs(gIn - _prevG);
            var dB = Math.Abs(bIn - _prevB);
            var maxDelta = Math.Max(dR, Math.Max(dG, dB));

            var bypassAlpha = TemporalIntegrator.BlendAlpha(_bypassFrames);

            switch (_state)
            {
                case State.Normal:
                    if (!_hasPrevious || maxDelta <= _transientThreshold)
                    {
                        _inner.Apply(ref r, ref g, ref b, profile);
                    }
                    else
                    {
                        _state = State.Bypass;
                        _settleCount = 0;
                        _inner.Apply(ref r, ref g, ref b, profile, bypassAlpha, bypassAlpha, bypassAlpha, bypassAlpha);
                        _bypassFrameCount++;
                    }

                    break;

                case State.Bypass:
                    _bypassFrameCount++;
                    if (maxDelta < _settleThreshold)
                    {
                        _settleCount++;
                        if (_settleCount >= _settleFrames)
                        {
                            _state = State.Recovery;
                            _recoveryFrame = 0;
                        }
                    }
                    else
                    {
                        _settleCount = 0;
                    }

                    _inner.Apply(ref r, ref g, ref b, profile, bypassAlpha, bypassAlpha, bypassAlpha, bypassAlpha);
                    break;

                case State.Recovery:
                    _bypassFrameCount++;
                    _recoveryFrame++;

                    if (_recoveryFrame >= 2)
                    {
                        _state = State.Normal;
                        _inner.Apply(ref r, ref g, ref b, profile);
                    }
                    else
                    {
                        var profileChromaAttack = TemporalIntegrator.BlendAlpha(profile.FramesChromaAttack);
                        var profileChromaDecay = TemporalIntegrator.BlendAlpha(profile.FramesChromaDecay);
                        var profileLumaAttack = TemporalIntegrator.BlendAlpha(profile.FramesLumaAttack);
                        var profileLumaDecay = TemporalIntegrator.BlendAlpha(profile.FramesLumaDecay);
                        _inner.Apply(ref r, ref g, ref b, profile,
                            (bypassAlpha + profileChromaAttack) / 2,
                            (bypassAlpha + profileChromaDecay) / 2,
                            (bypassAlpha + profileLumaAttack) / 2,
                            (bypassAlpha + profileLumaDecay) / 2);
                    }

                    break;
            }

            _prevR = rIn;
            _prevG = gIn;
            _prevB = bIn;
            _hasPrevious = true;
        }
        else
        {
            _inner.Apply(ref r, ref g, ref b, profile);

            _prevR = rIn;
            _prevG = gIn;
            _prevB = bIn;
            _hasPrevious = true;
        }
    }

    public void Reset()
    {
        _inner.Reset();
        _state = State.Normal;
        _settleCount = 0;
        _recoveryFrame = 0;
        _hasPrevious = false;
        _prevR = _prevG = _prevB = 0;
    }

    public void UpdateSettings(PipelineSettings settings)
    {
        _enabled = settings.TransientBypassEnabled;
        _integrationEnabled = settings.IntegrationEnabled;
        _transientThreshold = settings.TransientThreshold / 255.0;
        _settleThreshold = settings.SettleThreshold / 255.0;
        _settleFrames = settings.SettleFrames;
        _bypassFrames = Math.Clamp(settings.BypassCoefficient, 1, 256);
    }

    public (int bypassFrames, int totalFrames) GetBypassStats()
    {
        var stats = (_bypassFrameCount, _totalFrameCount);
        _bypassFrameCount = 0;
        _totalFrameCount = 0;
        return stats;
    }

    private enum State
    {
        Normal,
        Bypass,
        Recovery
    }
}