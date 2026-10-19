/*
 * Detector self-test for .github/scripts/questpdf-cpu-check.sh.
 * Each mode executes one instruction that an Ivy Bridge CPU does not have.
 * Under an emulated Ivy Bridge the process must die with SIGILL; if it does not,
 * the emulator cannot prove anything about the QuestPDF native library.
 * Run it only inside the harness container.
 */
#include <immintrin.h>
#include <stdio.h>
#include <string.h>

static int probe_avx2(void)
{
    volatile int seed = 3;
    __m256i a = _mm256_set1_epi32(seed);
    __m256i b = _mm256_set1_epi32(seed + 1);
    __m256i sum = _mm256_add_epi32(a, b);
    volatile int first = _mm256_extract_epi32(sum, 0);
    return first == 7;
}

static int probe_fma(void)
{
    volatile double seed = 2.0;
    __m128d a = _mm_set_sd(seed);
    __m128d b = _mm_set_sd(seed + 1.0);
    __m128d c = _mm_set_sd(seed + 2.0);
    volatile double result = _mm_cvtsd_f64(_mm_fmadd_sd(a, b, c));
    return result == 10.0;
}

static int probe_bmi2(void)
{
    volatile unsigned int value = 0xFFu;
    volatile unsigned int mask = 0xF0F0u;
    volatile unsigned int result = _pdep_u32(value, mask);
    return result == 0xF0F0u;
}

int main(int argc, char **argv)
{
    const char *mode = argc > 1 ? argv[1] : "avx2";

    if (strcmp(mode, "avx2") == 0 && probe_avx2())
    {
        puts("AVX2_EXECUTED");
        return 0;
    }

    if (strcmp(mode, "fma") == 0 && probe_fma())
    {
        puts("FMA_EXECUTED");
        return 0;
    }

    if (strcmp(mode, "bmi2") == 0 && probe_bmi2())
    {
        puts("BMI2_EXECUTED");
        return 0;
    }

    puts("PROBE_FAILED");
    return 3;
}
