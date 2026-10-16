# BeatLocator Recommendation Algorithm

This document describes how BeatLocator v0.1.1 turns a player's ranked score history into a single map and difficulty recommendation. It covers BeatLeader and ScoreSaber. Map download, launch, roulette presentation, and post-level PP tracking are separate systems.

The shared implementation lives in
[`RecommendationEngine`](BeatLocator/EvaluationManagers/RecommendationEngine.cs).
Provider-specific behavior lives in
[`BLEvaluationManager`](BeatLocator/EvaluationManagers/BLEvaluationManager.cs),
[`SSEvaluationManager`](BeatLocator/EvaluationManagers/SSEvaluationManager.cs),
[`BLWebUtil`](BeatLocator/WebUtils/BLWebUtil.cs), and
[`SSWebUtil`](BeatLocator/WebUtils/SSWebUtil.cs).

## Overview

For each ranking provider, BeatLocator:

1. Loads bounded recent and top ranked results, with a short provider cache.
2. Builds a player-specific relationship between map stars and achieved accuracy.
3. Converts that model into five difficulty targets.
4. Requests ranked candidates around the selected target, or samples a challenge catalogue for BeatLeader `END ME`.
5. Applies provider and user filters.
6. Scores every eligible map difficulty.
7. Selects one difficulty using weighted random selection.
8. Adds the selected map to the current session history.

The selected difficulty is final. The roulette animation presents that result;
it does not perform another random selection.

## Stable Player Skill

The pure estimator lives in
[`PlayerSkillEstimator`](BeatLocator/EvaluationManagers/PlayerSkillEstimator.cs).
BeatLeader loads up to 100 recent and 100 top-PP ranked scores, deduplicated by score ID.
ScoreSaber scans up to three recent pages and, when more history exists, up to
three top pages, retaining ranked CLEAR results. Original score timestamps are
preserved. Provider caches expire after two minutes and are isolated by player.

BeatLeader speed-modified observations use SS/FS/SF stars and component ratings
from `difficulty.modifiersRating`, together with raw accuracy. Unsupported modifiers
and missing speed ratings are excluded. Candidates also account for the active
BeatLeader speed setting, using actual API ratings for local eligibility checks.

### History and accuracy normalization

The estimator retains up to 240 timestamped observations, ordered newest first.
Failed attempts, unsupported modifiers and invalid ratings or accuracy values
are excluded. A play must have a usable star rating and accuracy above zero and
at most one. Speed-modified results require the corresponding actual ratings;
the model does not approximate them using a PP multiplier.

Recency weights multiply decay in elapsed days behind the newest observation
and number of newer observations:

```text
weight = 0.5 ^ (daysBehindNewest / 45) * 0.5 ^ (sortedIndex / 60)
```

Weights below 0.001 are discarded. There is no fixed skill penalty simply for
not playing for 30 days. Older evidence loses influence relative to newer plays.

Reference accuracy is the recency-weighted mean accuracy plus two percentage
points, clamped to 80-90%. The model fits a negative slope between stars and
logit accuracy, using `logit(a) = ln(a / (1 - a))`. Accuracy is clamped to
0.01-0.9995 for this calculation.

The default slope is -0.35. Fitting requires at least five comparable results
and enough star variance. Flat, positive or otherwise unusable slopes use the
default. Otherwise, fitted influence is `min(0.35, sampleCount / 100)` and the
regularized slope is clamped to -0.8 through -0.15.

Each result estimates capability at reference accuracy:

```text
reserve = (logit(accuracy) - logit(referenceAccuracy)) / -slope
reserve = clamp(reserve, -min(4, stars * 0.65), min(2.5, stars * 0.45))
capability = clamp(stars + reserve, 0.1, 25)
```

Ordinary difficulty uses a recency-weighted mean of
`stars + min(0.75, capability - stars)`. This limits inferred positive accuracy
reserve to 0.75 stars per result and describes the player's usual range rather
than an upper percentile of their best plays. The center is then capped at the
demonstrated challenge boundary described below.

Hard-challenge and component evidence uses up to 60 comparable results. Easy
practice has reduced influence on this evidence: its weight is multiplied by
`max(0.01, min(1, stars / anchor) ^ 3)`, where `anchor` is the weighted 90th
percentile of demonstrated stars. Ordinary difficulty keeps the original
recency weights. One valid comparable result is sufficient for a fallback profile.

## Difficulty Presets

The first four presets use accuracy anchors around the ordinary history center:

| Preset | Accuracy anchor |
|---|---|
| `SUPER EASY` | Reference + 7 percentage points, at most 97% |
| `EASY` | Reference + 4 percentage points, at most 94% |
| `OKAY` | Reference accuracy |
| `A BIT HARD` | Reference - 5 percentage points |

The star offset is `(logit(anchor) - logit(referenceAccuracy)) / slope`.
For `SUPER EASY`, its magnitude is limited to `min(3, max(0.12, center * 0.5))`.
The other ordinary presets use `min(1.75, max(0.12, center * 0.25))`.
`A BIT HARD` is also capped near demonstrated hard-clear capability.
All final targets are clamped to 0.1-25 stars.

`END ME` uses a separate weighted 85th-percentile boundary from recent comparable
clears with at least 70% accuracy. Each clear may contribute a positive accuracy
reserve of at most `min(0.75, stars * 0.15)`. With no qualifying hard clears, the
boundary falls back to the ordinary center. `END ME` starts at the greater of
the center and this boundary; it does not extrapolate a regression toward 80%
or 70% accuracy.

`Barely Possible` adds `min(0.75, max(0.06, boundary * 0.1))` stars.
`Actually Impossible` starts with `min(1.5, max(0.06, boundary * 0.2))` and applies
an additional proportional stretch for boundaries below eight stars:

```text
stretch = 0.2 + 0.2 * clamp((8 - boundary) / 3, 0, 1)
step = max(step, min(1.5, boundary * 0.2) + boundary * (stretch - 0.2))
```

BeatLeader selects `END ME` from sampled real candidates as described below.
ScoreSaber uses the same target estimator with its bounded star-window search.
Profiles and sparse-history fallbacks remain separate for each provider.

## Candidate Search

The first four BeatLeader presets and all ScoreSaber presets begin at the selected target with a requested `+-0.5` star window, reduced for weak players. If the provider returns maps but no eligible difficulty can be selected, the shared engine expands the window in 0.5-star steps:

```text
+-0.5 -> +-1.0 -> +-1.5 -> +-2.0
```

The search expands at most `min(2, max(0.15, target * 0.3))` stars from the calculated target. The sequence above applies when the cap is two stars. It returns a filter-change error instead of selecting a clearly unsuitable map. Transport or provider failures are not treated as empty candidate sets and do not trigger unlimited widening.

### BeatLeader Candidate Sampling

BeatLeader filters ranked maps by the current star range, played state, mode, and duration through its maps API. For a given filter query, BeatLocator keeps a page cursor and can inspect up to two provider pages at each star window when the first page has no selectable candidate.

For ordinary presets, the provider sort changes with the selected difficulty:

- `SUPER EASY`: stars ascending;
- middle presets: play count descending.

### BeatLeader END ME Catalogue

`END ME` samples up to two pages sorted by descending stars. Its effective-star
range starts at the ordinary history center and ends at
`desiredTarget + min(2, max(0.15, desiredTarget * 0.3))`, capped at 25 stars.
Duration, played state, characteristic, style, active speed, component
suitability and session history still restrict eligibility. This is a bounded
sample, not a scan of every ranked map.

If the desired target exceeds the eligible sample's ceiling, `Actually Impossible`
targets that ceiling. `Barely Possible` uses the candidate at index
`floor((candidateCount - 1) * 0.8)` in star order. A very small pool can make the
two settings converge. These catalogue adjustments do not lower stored skill.

The target is then snapped to the nearest eligible star value. Only candidates
within half of `bandwidth = min(0.35, max(0.06, target * 0.04))` participate.
Their style/component weights receive an additional proximity factor:

```text
challengeWeight = candidateWeight / (1 + abs(candidateStars - target) / bandwidth)
```

Component suitability is evaluated at each candidate's actual effective stars.
The engine returns an error if the challenge pool is empty instead of silently
falling back to an easy map.

### ScoreSaber Candidate Sampling

ScoreSaber requests ranked leaderboard difficulties in the current star range. It inspects one candidate sample at each star window. `SUPER EASY` and `END ME` use star sorting; the middle presets use total score count.

ScoreSaber leaderboard responses do not contain all required BeatSaver map metadata. BeatLocator resolves candidates by hash through BeatSaver before applying duration filtering and building recommendation maps.

## Search Filters

### Duration

Duration is a hard filter and never contributes bonus points to a candidate. The ranges are intentionally inclusive and overlap:

| Setting | Accepted duration |
|---|---|
| `ANY` | No restriction |
| `SHORT` | 0:00-1:00 |
| `SMALL` | 1:00-2:30 |
| `NORMAL` | 2:00-4:00 |
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
BeatLeaderWeight = (0.6 * styleWeight + 0.4 * difficultyScore) * profileSuitability
```

### BeatLeader Component Suitability

Acc, pass and tech budgets are weighted means of the corresponding ratings from
up to 60 comparable results. Each observed rating is scaled by
`clamp(capability / stars, 0.6, 1.3)`. A component needs at least three usable
observations; otherwise that component does not restrict candidates.

For a candidate, `scale = max(0.5, targetStars / center)` and each component ratio
is `candidateRating / (componentBudget * scale)`. Suitability starts at 1:

- Any known component ratio above 1.6 rejects the candidate.
- Pass and tech ratios above 1.1 multiply suitability by `(1.6 - ratio) / 0.5`.
- Acc ratios above 1.15 multiply suitability by `(1.6 - ratio) / 0.45`.

This reduces the chance that a strong accuracy profile receives an unsupported
pass or tech challenge merely because the overall star rating is close enough.

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
- no comparable ranked result with usable ratings is available;
- provider transport or authentication fails;
- filters remove all candidates within the bounded star range;
- every matching map has already appeared in the current session;
- ScoreSaber cannot safely verify a `New` candidate.

These outcomes are kept separate so filter exhaustion is not presented as a connection problem and provider failures do not silently weaken the requested settings.
