namespace BambiHeavy.Pipeline;

public static class Bt601
{
    // Forward
    public const double Y_R = 0.299011230469;
    public const double Y_G = 0.587011718750;
    public const double Y_B = 0.114013671875;

    public const double U_R = -0.168701171875;
    public const double U_G = -0.331298828125;
    public const double U_B = 0.500000000000;

    public const double V_R = 0.500000000000;
    public const double V_G = -0.418701171875;
    public const double V_B = -0.081298828125;

    // Inverse
    public const double INV_V_R = 1.402038574219;
    public const double INV_U_G = -0.343994140625;
    public const double INV_V_G = -0.714134216309;
    public const double INV_U_B = 1.772048950195;
}