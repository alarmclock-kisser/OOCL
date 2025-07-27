extern "C" __global__ void newtonAuto01(
    unsigned char* input,
    unsigned char* output,
    int width,
    int height,
    double zoom,
    float offsetX,
    float offsetY,
    int baseR,
    int baseG,
    int baseB,
    int iterCoeff)
{
    // Manuelle Definition der Konstanten
    #define PI 3.14159265358979323846
    #define TWO_PI_OVER_3 2.09439510239319549229 // (2π/3)

    int px = blockIdx.x * blockDim.x + threadIdx.x;
    int py = blockIdx.y * blockDim.y + threadIdx.y;

    if (px >= width || py >= height) return;

    int maxIter = 20 + static_cast<int>(iterCoeff * log(zoom + 1.0));
    double zx = (px - width/2.0) / (0.25 * zoom * width) + offsetX;
    double zy = (py - height/2.0) / (0.25 * zoom * height) + offsetY;

    int iter = 0;
    double tolerance = 1e-6 * (1.0 + zoom/1000.0);

    while (iter < maxIter)
    {
        double zx2 = zx*zx;
        double zy2 = zy*zy;
        double f_re = zx*(zx2 - 3*zy2) - 1.0;
        double f_im = zy*(3*zx2 - zy2);

        double df_re = 3*(zx2 - zy2);
        double df_im = 6*zx*zy;
        double df_norm = df_re*df_re + df_im*df_im;

        double delta_re = (f_re*df_re + f_im*df_im) / df_norm;
        double delta_im = (f_im*df_re - f_re*df_im) / df_norm;
        
        zx -= delta_re;
        zy -= delta_im;

        if (delta_re*delta_re + delta_im*delta_im < tolerance*tolerance)
            break;

        iter++;
    }

    // Wurzel-Identifikation mit manueller PI-Konstante
    double angle = atan2(zy, zx);
    int root = static_cast<int>((angle + PI)/TWO_PI_OVER_3) % 3;

    int idx = (py * width + px) * 4;
    float t = (float)iter / (float)maxIter;
    float fade = 1.0f - expf(-5.0f * t);

    switch(root)
    {
        case 0:
            output[idx+0] = baseR;
            output[idx+1] = static_cast<unsigned char>(baseG * fade);
            output[idx+2] = static_cast<unsigned char>(baseB * fade);
            break;
        case 1:
            output[idx+0] = static_cast<unsigned char>(baseR * fade);
            output[idx+1] = baseG;
            output[idx+2] = static_cast<unsigned char>(baseB * fade);
            break;
        case 2:
            output[idx+0] = static_cast<unsigned char>(baseR * fade);
            output[idx+1] = static_cast<unsigned char>(baseG * fade);
            output[idx+2] = baseB;
            break;
    }
    output[idx+3] = 255;
}
