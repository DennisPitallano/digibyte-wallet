// Reference harness: runs DigiByte Core's sph implementations and prints intermediate values.
// Usage: qubit_ref < lines_of_hex   (each line is hex of the input to hash)
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <iostream>
extern "C++" {
#include "crypto/sph_luffa.h"
#include "crypto/sph_cubehash.h"
#include "crypto/sph_shavite.h"
#include "crypto/sph_simd.h"
#include "crypto/sph_echo.h"
}

static std::vector<unsigned char> unhex(const std::string& s) {
    std::vector<unsigned char> out;
    for (size_t i = 0; i + 1 < s.size(); i += 2) out.push_back((unsigned char)std::stoi(s.substr(i, 2), nullptr, 16));
    return out;
}
static void pr(const char* tag, const unsigned char* p, size_t n) {
    printf("%s ", tag);
    for (size_t i = 0; i < n; i++) printf("%02x", p[i]);
    printf("\n");
}

int main() {
    std::string line;
    while (std::getline(std::cin, line)) {
        if (line.empty()) continue;
        auto in = unhex(line == "-" ? std::string() : line);
        unsigned char h[5][64];
        static unsigned char blank[1];
        const void* data = in.empty() ? (const void*)blank : (const void*)in.data();

        // Each primitive on the raw input, for per-step known answers.
        { sph_luffa512_context c; sph_luffa512_init(&c); sph_luffa512(&c, data, in.size()); sph_luffa512_close(&c, h[0]); pr("luffa", h[0], 64); }
        { sph_cubehash512_context c; sph_cubehash512_init(&c); sph_cubehash512(&c, data, in.size()); sph_cubehash512_close(&c, h[1]); pr("cubehash", h[1], 64); }
        { sph_shavite512_context c; sph_shavite512_init(&c); sph_shavite512(&c, data, in.size()); sph_shavite512_close(&c, h[2]); pr("shavite", h[2], 64); }
        { sph_simd512_context c; sph_simd512_init(&c); sph_simd512(&c, data, in.size()); sph_simd512_close(&c, h[3]); pr("simd", h[3], 64); }
        { sph_echo512_context c; sph_echo512_init(&c); sph_echo512(&c, data, in.size()); sph_echo512_close(&c, h[4]); pr("echo", h[4], 64); }

        // The Qubit chain.
        unsigned char q[5][64];
        { sph_luffa512_context c; sph_luffa512_init(&c); sph_luffa512(&c, data, in.size()); sph_luffa512_close(&c, q[0]); }
        { sph_cubehash512_context c; sph_cubehash512_init(&c); sph_cubehash512(&c, q[0], 64); sph_cubehash512_close(&c, q[1]); }
        { sph_shavite512_context c; sph_shavite512_init(&c); sph_shavite512(&c, q[1], 64); sph_shavite512_close(&c, q[2]); }
        { sph_simd512_context c; sph_simd512_init(&c); sph_simd512(&c, q[2], 64); sph_simd512_close(&c, q[3]); }
        { sph_echo512_context c; sph_echo512_init(&c); sph_echo512(&c, q[3], 64); sph_echo512_close(&c, q[4]); }
        for (int i = 0; i < 5; i++) { char tag[16]; snprintf(tag, sizeof tag, "chain%d", i); pr(tag, q[i], 64); }
        printf("--\n");
    }
    return 0;
}
