# BeatLocator Recommendation Algorithm

This document describes how BeatLocator currently turns a player's ranked score history into a single map and difficulty recommendation. It covers the recommendation engine only. Map download, launch, roulette presentation, and post-level PP tracking are separate systems.

The shared implementation lives in
[`RecommendationEngine`](BeatLocator/EvaluationManagers/RecommendationEngine.cs).
Provider-specific behavior lives in
[`BLEvaluationManager`](BeatLocator/EvaluationManagers/BLEvaluationManager.cs),
[`SSEvaluationManager`](BeatLocator/EvaluationManagers/SSEvaluationManager.cs),
[`BLWebUtil`](BeatLocator/WebUtils/BLWebUtil.cs), and
[`SSWebUtil`](BeatLocator/WebUtils/SSWebUtil.cs).

## Overview

For each ranking provider, BeatLocator:

1. Loads up to 100 valid ranked results from the player's profile.
2. Builds a player-specific relationship between map stars and achieved accuracy.
3. Converts that model into five difficulty targets.
4. Requests ranked candidates around the selected target.
5. Applies provider and user filters.
6. Scores every eligible map difficulty.
7. Selects one difficulty using weighted random selection.
8. Adds the selected map to the current session history.

The selected difficulty is final. The roulette animation presents that result;
it does not perform another random selection.

## Player Score Data

The skill model uses at most 100 valid ranked plays per provider. A play is usable when it contains a positive star rating, a usable timestamp, and an accuracy strictly between 0 and 100 percent.

### BeatLeader

BeatLocator requests the player's 100 most recent ranked scores, ordered by date. The signed-in player ID is obtained through the installed BeatLeader mod.

### ScoreSaber

ScoreSaber does not provide a ranked-only filter for a player's score history. BeatLocator therefore scans a bounded number of pages:

- up to three pages ordered by `recent`;
- if fewer than 100 valid ranked clears were found and more history exists, up
  to three pages ordered by `top`;
- duplicate score IDs, unranked entries, and outcomes other than `CLEAR` are
  ignored.

This keeps profile loading bounded while still reaching ranked scores when ordinary saved plays occupy the newest pages.

## Play Weighting

Each valid play becomes a sample containing stars, accuracy, and a weight.
BeatLocator combines a recency weight and an accuracy-quality weight:

```text
dateWeight = 0.5 ^ (ageInDays / 30)
sampleWeight = 0.3 * dateWeight + 0.7 * accuracyWeight
```

The recency component has a 30-day half-life. 
The accuracy component reduces the influence of very low clears and unusually high scores:

| Accuracy | Accuracy weight |
|---|---:|
| Below 70% | 0.10 |
| 70% to below 80% | 0.50 |
| 80% to 97% | 1.00 |
| Above 97% to 99% | 0.60 |
| Above 99% | 0.25 |

*At least five valid samples are required.*

## Skill Model

Accuracy is converted to logit space:

```text
logitAccuracy = ln(accuracy / (1 - accuracy))
```

BeatLocator then fits a weighted linear model:

```text
logitAccuracy = intercept + slope * stars
```

The fitted slope must be meaningfully negative, because accuracy is expected to decrease as map difficulty increases. Model creation fails when the samples have too little star variation or produce a slope greater than or equal to `-0.01`.

The center of the player's range is the weighted average star rating of the valid samples, clamped to the supported 1–15 star range.

## Difficulty Presets

The five UI presets are points on the fitted accuracy curve relative to the player's center:

| Preset | Target logit relative to center | Maximum distance from center |
|---|---:|---:|
| `SUPER EASY` | +0.75 | 3.00 stars |
| `EASY` | +0.35 | 1.75 stars |
| `OKAY` | center | 0 stars |
| `A BIT HARD` | -0.25 | 1.75 stars |
| `END ME` — Barely Possible | -0.35 | 3.00 stars |
| `END ME` — Actually Impossible | -0.75 | 3.00 stars |

A positive logit offset targets higher expected accuracy and therefore an easier map. A negative offset targets lower expected accuracy and therefore a harder map. Every result is also clamped to 1–15 stars.

The calculated range is cached separately for BeatLeader and ScoreSaber. When the `END ME` behavior is changed in settings, cached score samples are reused to recalculate the range without downloading the profile again.

## Candidate Search

Every search begins at the selected target with a `±0.5` star window. If the provider returns maps but no eligible difficulty can be selected, the shared engine expands the window in 0.5-star steps:

```text
±0.5 -> ±1.0 -> ±1.5 -> ±2.0
```

The search never expands farther than two stars from the calculated target. It returns a filter-change error instead of selecting a clearly unsuitable map. Transport or provider failures are not treated as empty candidate sets and do not trigger unlimited widening.

### BeatLeader Candidate Sampling

BeatLeader filters ranked maps by the current star range, played state, mode, and duration through its maps API. For a given filter query, BeatLocator keeps a page cursor and can inspect up to two provider pages at each star window when the first page has no selectable candidate.

The provider sort changes with the selected difficulty preset:

- `SUPER EASY`: stars ascending;
- `END ME`: stars descending;
- middle presets: play count descending.

### ScoreSaber Candidate Sampling

ScoreSaber requests ranked leaderboard difficulties in the current star range. It inspects one candidate sample at each star window. `SUPER EASY` and `END ME` use star sorting; the middle presets use total score count.

ScoreSaber leaderboard responses do not contain all required BeatSaver map metadata. BeatLocator resolves candidates by hash through BeatSaver before applying duration filtering and building recommendation maps.

## Search Filters

### Duration

Duration is a hard filter and never contributes bonus points to a candidate. The ranges are intentionally inclusive and overlap:

| Setting | Accepted duration |
|---|---|
| `ANY` | No restriction |
| `SHORT` | 0:00–1:00 |
| `SMALL` | 1:00–2:30 |
| `NORMAL` | 2:00–4:00 |
| `LONG` | 4:00 and longer |

BeatLeader applies these bounds in the provider request. ScoreSaber candidates are filtered after their duration has been resolved through BeatSaver.

### Played State

BeatLeader requests either `played` or `unplayed` maps directly from its API.
The `Played By Me Before` toggle selects which of those two sets is requested.

ScoreSaber provides three modes:

- `Any`: no per-map played-state restriction;
- `Played`: load cleared entries from the first and up to two randomly selected
  pages of the player's score history, then choose matching ranked difficulties;
- `New`: sample a random matching leaderboard page and verify candidates using
  ScoreSaber's exact player/hash/mode/difficulty endpoint.

For the `New` filter, only a not-found response is treated as unplayed. A successful `CLEAR` response is treated as played. Rate limits and other API errors fail the filter safely instead of incorrectly classifying a map as new. Checks are serialized, spaced by at least 250 ms, retried in a bounded manner, cached for the session, and limited to 120 unique candidates per search.

### Two Saber Only

When enabled, only difficulties whose provider mode contains `Standard` are eligible. Other characteristics such as One Saber, 90 Degree, and 360 Degree are excluded.

### Secret Difficulty

`Secret Difficulty` is a presentation option, not a recommendation filter. It hides the selected difficulty from the roulette result until the player starts the map. It does not change candidate loading or scoring.

## Difficulty Score

Both providers begin with the same proximity score for a candidate star rating
`candidateStars` and the selected `targetStars`:

```text
difference = abs((candidateStars - targetStars) /
                 (candidateStars + targetStars))
difficultyScore = clamp(1 - difference / 1.5, 0, 1)
```

The provider-specific evaluator then produces the final selection weight.

### BeatLeader Style Score

BeatLeader exposes pass and tech ratings. A normalized style value is computed
for every difficulty:

```text
style = (techRating - passRating) / (techRating + passRating)
```

Positive values are tech-oriented, values around zero are balanced, and negative values are pass-oriented. Difficulties without both ratings are not eligible.

For a style value `s`, the five balance presets use these weights:

| Preset | Style weight |
|---|---|
| Extreme Tech | `1` at `s >= 0.5`; `s / 0.5` for `0 < s < 0.5`; otherwise `0` |
| Tech | `1` at `s >= 0.3`; scales from `0.5` to `1` for `0 <= s < 0.3`; has a small tail for `-0.1 < s < 0`; otherwise `0` |
| Balanced | `clamp(1 - abs(s) / 0.15, 0, 1)` |
| Pass | Mirror of Tech around zero |
| Extreme Pass | Mirror of Extreme Tech around zero |

If the style weight is zero, the difficulty is excluded. Otherwise:

```text
BeatLeaderWeight = 0.6 * styleWeight + 0.4 * difficultyScore
```

### ScoreSaber Score

ScoreSaber does not publish separate pass and tech ratings. Its final weight is therefore the shared difficulty proximity score. This is why the Tech/Pass balance selector is available only for BeatLeader.

**If you know of a reliable way to estimate Tech/Pass balance from data available through ScoreSaber, please [open a GitHub issue](https://github.com/kalbuus/BeatLocator/issues).**

## Session History

Before scoring, BeatLocator removes maps already selected during the current Beat Saber session. Maps are identified using all available stable keys:

- map hash;
- provider map ID;
- download URL;
- song name and mapper as a last-resort fallback.

The map is added to history immediately after selection, even if the player later skips it or the download fails. Session history is held in memory and is cleared when Beat Saber restarts.

## Weighted Selection

Every eligible difficulty with a finite positive weight participates in a weighted random draw:

```text
selectionPoint = random(0, sumOfWeights)
```

The engine walks the candidate list and subtracts each weight until the point falls below zero. Higher-scoring difficulties are more likely to be selected, but the best-scoring difficulty is not guaranteed to win every time.

For ScoreSaber `New`, a weighted candidate is drawn first and then checked for played state. Played candidates are removed and the weighted draw repeats over the remaining candidates until a new map is found, the candidates are exhausted, or the check budget is reached.

Once a candidate passes all checks, its map and exact provider difficulty are stored as the recommendation result. Download and launch use that same result.

## Failure Conditions

The engine returns a user-facing failure instead of a recommendation when, for example:

- the provider score history cannot be loaded;
- fewer than five usable ranked clears are available;
- the samples cannot produce a meaningful decreasing accuracy curve;
- provider transport or authentication fails;
- filters remove all candidates within the bounded star range;
- every matching map has already appeared in the current session;
- ScoreSaber cannot safely verify a `New` candidate.

These outcomes are kept separate so filter exhaustion is not presented as a connection problem and provider failures do not silently weaken the requested settings.
