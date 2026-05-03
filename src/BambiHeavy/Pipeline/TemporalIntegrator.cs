// Temporal Integration — YUV-space IIR filter
//
// Problem: raw per-frame ambient colors contain noise (compression artifacts,
// sensor noise, UI flicker). LEDs responding to every frame produce visible
// color jitter that is fatiguing. This filter smooths across frames so LEDs
// reflect the sustained average color, not momentary noise.
//
// Design: converts each frame to BT.601 YUV, applies a first-order IIR low-pass
// independently to Y (luminance) and U/V (chrominance), then converts back to RGB.
// YUV separation allows independent time constants for brightness vs color,
// matching human visual sensitivity (more sensitive to brightness changes).
//
// Attack/decay asymmetry: brightness increases and color saturation increases
// are tracked faster than decreases. Sudden brightening is jarring if lagged;
// gradual dimming benefits from extra smoothing.

namespace BambiHeavy.Pipeline;

public class TemporalIntegrator
{
    private bool _initialized;
    private double _yPrev, _uPrev, _vPrev;

    public TemporalIntegrator()
    {
        _initialized = false;
    }

    public void Reset()
    {
        _initialized = false;
        _yPrev = _uPrev = _vPrev = 0;
    }

    // Convert frame count to blend fraction: alpha = 1 / framesToConverge.
    // E.g. 2 frames → 0.5 (50% blend), 8 frames → 0.125 (12.5% blend).
    public static double BlendAlpha(double frames)
    {
        return 1.0 / frames;
    }

    public void Apply(ref double r, ref double g, ref double b, TuningProfile profile,
        double? overrideAlphaChromaAttack = null, double? overrideAlphaChromaDecay = null,
        double? overrideAlphaLumaAttack = null, double? overrideAlphaLumaDecay = null)
    {
        if (profile == null)
            // No profile — pass through
            return;

        double rCur = r, gCur = g, bCur = b;

        // RGB → YUV (BT.601)
        var yCur = Bt601.Y_R * rCur + Bt601.Y_G * gCur + Bt601.Y_B * bCur;
        var uCur = Bt601.U_R * rCur + Bt601.U_G * gCur + Bt601.U_B * bCur;
        var vCur = Bt601.V_R * rCur + Bt601.V_G * gCur + Bt601.V_B * bCur;

        // First frame: initialize accumulator and pass through
        if (!_initialized)
        {
            _yPrev = yCur;
            _uPrev = uCur;
            _vPrev = vCur;
            _initialized = true;
            return;
        }

        double alphaChromaAttack, alphaChromaDecay, alphaLumaAttack, alphaLumaDecay;
        if (overrideAlphaChromaAttack.HasValue && overrideAlphaChromaDecay.HasValue &&
            overrideAlphaLumaAttack.HasValue && overrideAlphaLumaDecay.HasValue)
        {
            alphaChromaAttack = overrideAlphaChromaAttack.Value;
            alphaChromaDecay = overrideAlphaChromaDecay.Value;
            alphaLumaAttack = overrideAlphaLumaAttack.Value;
            alphaLumaDecay = overrideAlphaLumaDecay.Value;
        }
        else
        {
            alphaChromaAttack = BlendAlpha(profile.FramesChromaAttack);
            alphaChromaDecay = BlendAlpha(profile.FramesChromaDecay);
            alphaLumaAttack = BlendAlpha(profile.FramesLumaAttack);
            alphaLumaDecay = BlendAlpha(profile.FramesLumaDecay);
        }

        var dY = yCur - _yPrev;
        var dU = uCur - _uPrev;
        var dV = vCur - _vPrev;

        var alphaY = dY <= 0 ? alphaLumaDecay : alphaLumaAttack;
        var alphaU = Math.Abs(uCur) <= Math.Abs(_uPrev) ? alphaChromaDecay : alphaChromaAttack;
        var alphaV = Math.Abs(vCur) <= Math.Abs(_vPrev) ? alphaChromaDecay : alphaChromaAttack;

        var newY = _yPrev + alphaY * dY;
        var newU = _uPrev + alphaU * dU;
        var newV = _vPrev + alphaV * dV;

        _yPrev = newY;
        _uPrev = newU;
        _vPrev = newV;

        // YUV → RGB (BT.601 inverse)
        var rRaw = newY + Bt601.INV_V_R * newV;
        var gRaw = newY + Bt601.INV_U_G * newU + Bt601.INV_V_G * newV;
        var bRaw = newY + Bt601.INV_U_B * newU;

        r = Math.Clamp(rRaw, 0.0, 1.0);
        g = Math.Clamp(gRaw, 0.0, 1.0);
        b = Math.Clamp(bRaw, 0.0, 1.0);
    }
}