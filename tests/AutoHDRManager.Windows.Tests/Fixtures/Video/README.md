# Original video metadata fixtures

These six tiny files contain one original 64×64 gray frame, 10-bit HEVC in MKV/MP4. They contain no audio, third-party movie, user path or viewing history. They are project test assets under MIT, generated locally for this test suite.

The generator was FFmpeg 9.0.2 (development-only, never included in Auto HDR Manager). Neither FFmpeg nor an encoder is required to run these tests or use the app.

Equivalent command for each fixture, substituting `P`/`T` and extension:

```text
ffmpeg -f lavfi -i color=c=gray:s=64x64:r=1 -frames:v 1 -c:v libx265 -preset ultrafast -pix_fmt yuv420p10le -x265-params log-level=error:pools=1:frame-threads=1:info=0:colorprim=P:transfer=T:colormatrix=P -color_primaries P -color_trc T -colorspace P -map_metadata -1 output.mkv
```

| Fixture | P | T |
| --- | --- | --- |
| hdr10 | 9 (BT.2020) | 16 (PQ) |
| hlg | 9 (BT.2020) | 18 (HLG) |
| sdr | 1 (BT.709) | 1 (BT.709) |

These are metadata/parser tests, not a display quality reference. HDR10 classification requires explicit PQ + BT.2020 + at least 10 bits; static mastering metadata is not inferred or invented. Separate generated header-only tests verify conservative fallback when coded bit depth is unavailable.
