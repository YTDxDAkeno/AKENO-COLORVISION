# Color science

This document explains what Akeno ColorVision does to colors, why, and where the formulas come from.

## 1. The problem, in the user's words

> "Basically I don't see most shades of red and green, it's just gray … Different shades blend together
> and look the same. Some look brownish and some look more grayish."

People with red-green color vision deficiency (CVD) have a missing or shifted L ("red") or M ("green")
cone. The **red–green opponent signal** (roughly L − M) is weak or absent, while the **lightness** signal
(L + M) and the **blue–yellow** signal (S − (L + M)) mostly remain. Colors that differ mainly along the
red–green axis therefore collapse into similar browns, olives and grays — exactly the description above.

**Simulation vs. correction.** These are two different operations:

1. *Simulation* shows a person with typical vision how colors may appear with a CVD. The app uses
   simulation only internally, to **design and evaluate** corrections.
2. *Correction / accessibility enhancement* changes the colors on screen so that information hidden in the
   red–green axis is moved into channels the user can still see. That is what the app applies.

Neither can restore missing cone responses. Success is measured by one question: **can she now tell apart
colors that looked identical before?**

## 2. The constraint: one affine matrix

The Windows Magnification API applies one 5×5 matrix to the composed desktop. With alpha passed through,
that is an **affine RGB map** `out = A · in + t` (3×3 matrix plus offset), applied to the **gamma-encoded**
desktop values, followed by clamping to [0, 1]. Non-linear curves, look-up tables or hue-selective edits are
impossible with this backend, so every correction in the app is built only from operations a matrix can
represent. Every slider in the app maps to one of them.

## 3. Building blocks

All functions live in `src/Akeno.ColorVision.ColorScience` and are unit-tested.

| Block | Formula | Notes |
|---|---|---|
| **Red–green → blue/yellow** | `B += k_b (R − G)` | Reds gain blue (purple/pink), greens lose blue (more yellow). Grays (R = G) unchanged. |
| **Red–green → lightness** | `R,G,B += k_l (R − G)` | Positive: reds lighter, greens darker. Turns a hue difference into a lightness difference. |
| **Daltonization** | `D = I + E (I − S)` | Fidaner et al. (2005); `S` = Machado simulation matrix, `E = [[0,0,0],[0.7,1,0],[0.7,0,1]]`. Moves the information a simulated observer loses into channels they keep. |
| **Primary hue remap** | red primary rotated by θ_r, green by θ_g, blue = (1,1,1) − red − green | Keeps grays neutral; equal angles reduce exactly to a global hue rotation (tested). |
| **Saturation** | luma-preserving (Rec. 709 weights), as CSS `saturate()` | |
| **Contrast** | `out = c (in − 0.5) + 0.5` | |
| **Brightness** | `out = in + b` | |

Composition (`CorrectionPipeline.Build`):

```
discrimination = PrimaryHue ∘ OpponentTransfer ∘ Daltonization
blended        = lerp(Identity, discrimination, intensity)      ← "Correction strength"
final          = Brightness ∘ Contrast ∘ Saturation ∘ blended
```

Matrices are combined algebraically (associativity and the composition order are unit-tested) and
converted to Microsoft's row-vector MAGCOLOREFFECT layout in exactly one place.

### Simulation model

Machado, Oliveira & Fernandes (2009), *A Physiologically-based Model for Simulation of Color Vision
Deficiency*, IEEE TVCG 15(6). The tabulated matrices (protanomaly, deuteranomaly, tritanomaly; severity
0.0–1.0 in steps of 0.1) operate on **linear** RGB and are linearly interpolated between steps, as the
authors recommend. The table in `MachadoCvdModel.cs` was cross-checked digit-for-digit against the
BSD-licensed `colour-science` dataset (v0.4.7); a unit test checks that every row sums to 1.

### Linear vs. gamma-encoded application

Daltonization is defined in linear light, but the desktop filter works on encoded values. Two options
were evaluated with the PresetLab tool (deutan/protan severity 0.8, general pairs):

| Variant | RMS error vs. exact linear-light result | Mean simulated ΔE00 after correction |
|---|---|---|
| Apply `D` directly to encoded values | 0.079 (deutan), 0.084 (protan) | 11.8 (deutan), 15.5 (protan) |
| Best gray-preserving least-squares fit in the encoded domain | 0.083, 0.089 | 10.3, 13.3 |

The direct application is as close to the exact result as the best possible fit and separates colors
slightly more, so the presets use it (the fitting code remains for analysis). Both keep grays neutral.

## 4. Measuring improvement

`DiscriminationEvaluator` estimates what a CVD observer would perceive *after* the filter:

1. apply the filter to both colors of a pair (encoded domain, clamped — like the desktop pipeline),
2. simulate the observer with the Machado model in linear RGB,
3. measure the difference with **CIEDE2000** (Sharma, Wu & Dalal 2005; the implementation passes their
   published test data).

Pairs with ΔE00 < 8 are counted as *hard to tell apart*. Because the user's exact vision is unknown, results
are averaged over six observers: protan and deutan at severities 0.6, 0.8 and 1.0. *Distortion* is the mean
ΔE00 between original and filtered colors for typical vision across the RGB cube — a rough "how unnatural"
measure.

**Test sets** (`TestColorSets`):
* *General pairs* (18): the calibration pairs (red/green, dark red/brown, orange/green, muted green/gray,
  red/brown, green shades, red shades, pink/gray, purple/blue, teal/gray, brown/olive, dark-red block/brown
  block) plus traffic-light, berry/leaf, rust/moss, salmon/khaki, maroon/dark green and health-bar pairs.
* *Nether-inspired pairs* (15): approximate average colors resembling netherrack, crimson nylium, nether
  wart, nether bricks, soul sand/soil, basalt, blackstone, warped nylium, magma, ancient debris, etc.
  Only pairs that typical vision separates by color (ΔE00 ≳ 7.5) are included; pairs that differ mainly by
  texture (ore specks) are not a color-vision problem. These are original approximate values — no game
  textures are used or distributed.

## 5. Presets and results

Parameters were chosen with `tools/Akeno.ColorVision.PresetLab` (grid search + manual review), balancing
separation against distortion. Results from the simulation model (reproduce with
`dotnet run --project tools/Akeno.ColorVision.PresetLab -- markdown`):

| Preset | General pairs: mean ΔE00 (before → after) | Hard pairs (<8) | Nether-inspired pairs: mean ΔE00 | Hard pairs (<8) | Distortion for typical vision |
|---|---|---|---|---|---|
| Red-Green Assistance | 10.1 → 22.8 | 9 → 2 of 18 | 10.1 → 11.0 | 4 → 4 of 15 | 9.6 |
| Protan Assistance | 10.1 → 17.8 | 9 → 2 of 18 | 10.1 → 10.1 | 4 → 5 of 15 | 12.0 |
| Deutan Assistance | 10.1 → 21.4 | 9 → 2 of 18 | 10.1 → 10.6 | 4 → 5 of 15 | 9.9 |
| Strong Separation | 10.1 → 33.8 | 9 → 0 of 18 | 10.1 → 13.8 | 4 → 4 of 15 | 16.0 |
| Brightness & Contrast | 10.1 → 10.6 | 9 → 7 of 18 | 10.1 → 13.0 | 4 → 4 of 15 | 6.1 |
| Minecraft — Nether Visibility | 10.1 → 26.2 | 9 → 2 of 18 | 10.1 → 16.6 | 4 → 2 of 15 | 12.8 |

Preset parameters:

| Preset | Intensity | Parameters |
|---|---|---|
| Red-Green Assistance | 0.7 | blue/yellow 0.8, lightness +0.15, saturation 1.1 |
| Protan Assistance | 0.8 | daltonization (protan 0.8), blue/yellow 0.3, lightness +0.15, saturation 1.05 |
| Deutan Assistance | 0.8 | daltonization (deutan 0.8), blue/yellow 0.4, saturation 1.05 |
| Strong Separation | 1.0 | blue/yellow 1.1, lightness +0.3, saturation 1.25, contrast 1.05 |
| Brightness & Contrast | — | saturation 1.35, contrast 1.2, brightness +0.02 (no remapping) |
| Minecraft — Nether Visibility | 1.0 | blue/yellow 0.6, lightness −0.25, red hue +10°, saturation 1.45, contrast 1.25, brightness +0.07 |

Honest reading of these numbers:

* They are **model estimates** for idealised observers on an ideal sRGB display, not measurements of a
  person. The calibration step exists because real vision and real screens differ.
* Dark scenes are hard for any linear filter: dark colors have small RGB values, so the Nether preset relies
  on contrast and brightness as well. Pairs that are nearly black (dark brick vs black stone) stay difficult.
* Moving red–green differences onto the blue–yellow axis can make a few pairs that differ in blue slightly
  more similar (for example *two shades of green*, *purple vs blue*). This is an inherent trade-off of any
  3×3 correction; the strength slider and calibration let the user pick the balance.
* The protan/deutan presets slightly *increase* the number of hard Nether-inspired pairs (4 → 5); for
  Minecraft the dedicated preset is clearly better.

## 6. Calibration analysis

`CalibrationAnalyzer` compares the user's 0–3 ratings ("clearly different" … "exactly the same") with
ratings predicted from simulated ΔE00 (thresholds 4 / 8 / 15) for protan and deutan observers at severities
0.2–1.0, and for typical vision. The closest model (smallest total rating error) becomes the **starting
point** for the comparison step; when protan and deutan fit about equally well, the balanced red-green
correction is suggested. Two control pairs (blue/yellow, dark/light gray) detect display problems.

The wording never names a diagnosis; the record saved in the profile contains only the user's own answers,
the suggested starting point and her choice. The comparison rounds (strategy → strength → fine-tune) are
decided by what she finds easiest — the model only orders the options.

## 7. Color naming

The identifier names colors with OKLab/OKLCh (Björn Ottosson, 2020): a rule-based category (red, orange,
brown, yellow, green, cyan, blue, purple, pink, gray, black, white — with lightness/chroma rules for brown,
olive and pink) plus the nearest entry of ~140 everyday names *within the same category*, so the name and
category never contradict each other (tested across the RGB cube). Descriptors such as "dark", "muted" or
"vivid" come from OKLCh lightness and chroma.

## 8. Validation and safety

`TransformValidator` rejects matrices with non-finite values, |coefficients| > 4, |offsets| > 0.6, a
black/white luminance difference below 0.30, or |determinant| < 0.02, and warns about tinted grays or
washed-out blacks. Every preset is validated at its default and at full strength in the tests.

## 9. References

1. G. M. Machado, M. M. Oliveira, L. A. F. Fernandes. *A Physiologically-based Model for Simulation of
   Color Vision Deficiency.* IEEE Transactions on Visualization and Computer Graphics 15(6):1291–1298, 2009.
2. O. Fidaner, P. Lin, N. Ozguven. *Analysis of Color Blindness.* Stanford University EE368 project, 2005
   (basis of the daltonize algorithm).
3. G. Sharma, W. Wu, E. N. Dalal. *The CIEDE2000 color-difference formula: Implementation notes,
   supplementary test data, and mathematical observations.* Color Research & Application 30(1):21–30, 2005.
4. IEC 61966-2-1:1999. *Multimedia systems and equipment — Colour measurement and management — Default RGB
   colour space — sRGB.*
5. B. Ottosson. *A perceptual color space for image processing (OKLab).* 2020.
6. Microsoft. *Magnification API overview* and *MagSetFullscreenColorEffect function* (learn.microsoft.com).
7. Colour Developers. *colour-science*, `colour.blindness.datasets.machado2010` (BSD-3-Clause), used to
   cross-check the Machado tables.
