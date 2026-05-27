# MajdataPlay — Critical & High Severity Fixes (Design)

**Date**: 2026-05-27
**Author**: Claude (driven by @kogekiplay)
**Base**: `LingFeng-bbben/MajdataPlay@3ad563f7` (dev)
**Branch**: `fix/critical-and-high` on `kogekiplay/MajdataPlay`
**Strategy**: single branch, one commit per fix, 15 commits total. Push to fork only — no upstream PR in this pass.

---

## 0. Goals and Non-Goals

**Goals**

- Fix 5 Critical and 10 High severity issues identified in the 2026-05-27 review.
- Each fix is small, self-contained, semantically minimal (only the bug, no drive-by refactors).
- Each commit's message captures: severity, what, why, evidence file path.
- No behavior change for end users beyond bug removal — no new options, no API changes.

**Non-Goals**

- No Medium / Low issues this pass.
- No editor / CI fixes this pass (M11–M14 deferred).
- No automated test infrastructure (project has none; adding a test asmdef is out of scope for this branch).
- No Unity Editor builds — verification is by static review only (we cannot run Unity locally).
- No changes to submodules (`Assets/Plugins/HidSharp`, `ManagedBass`, `UniTask`).

---

## 1. Scope: The 15 Fixes

Listed in commit order. Order chosen so that earlier commits don't depend on later ones, and grouped by subsystem to minimize cognitive switching.

### Group A — Game core / judgement (3 commits)

| # | Severity | Title | File(s) |
|---|----------|-------|---------|
| 1 | Critical | C1: Stop applying `DisplayOffset` twice in note timing | `Assets/Scripts/Scenes/Game/GamePlayManager.cs` + `Assets/Scripts/Scenes/Game/NoteBehaviours/NoteDrop.cs` |
| 2 | High | H1: Derive Touch `JudgableRange` upper bound from `TOUCH_JUDGE_GOOD_AREA_MSEC` constant | `Assets/Scripts/Scenes/Game/NoteBehaviours/TouchDrop.cs` |
| 3 | High | H9: Log/assert on double-release in `NotePool.Bucket.Return` | `Assets/Scripts/Scenes/Game/Buffers/NotePool.cs` |

### Group B — NotePool memory (1 commit)

| # | Severity | Title | File(s) |
|---|----------|-------|---------|
| 4 | Critical | C3: Stop leaking `ArrayPool` rentals in `NotePool` and `Bucket` Dispose | `Assets/Scripts/Scenes/Game/Buffers/NotePool.cs` |

### Group C — IO initialization (3 commits)

| # | Severity | Title | File(s) |
|---|----------|-------|---------|
| 5 | Critical | C2: Fix inverted `Interlocked.CompareExchange` in 3 Init paths | `Assets/Scripts/IO/InputManager/InputManager.TouchPanel.cs`, `Assets/Scripts/IO/InputManager/InputManager.ButtonRing.cs`, `Assets/Scripts/IO/OutputManager/RawDeviceHandle/OutputManager.LedDevice.cs` |
| 6 | High | H4: Make HID `Read`/`Write` cancellable via `token.Register(stream.Dispose)` | same files as #5 + neighbours |
| 7 | High | H6: Set `_isInited = true` only after `AudioManager.Init` succeeds | `Assets/Scripts/IO/AudioManager.cs` |

### Group D — Audio safety (3 commits)

| # | Severity | Title | File(s) |
|---|----------|-------|---------|
| 8 | High | H2: Remove `~BassAudioSample` finalizer (or guard against post-Bass.Free calls) | `Assets/Scripts/IO/Base/Audio/BassAudioSample.cs` |
| 9 | High | H3: Track and free pinned `GCHandle` in `BassAudioSample` | `Assets/Scripts/IO/Base/Audio/BassAudioSample.cs` |
| 10 | High | H7: Guard against `channelmax == 0` divide-by-zero in normalization | `Assets/Scripts/IO/Base/Audio/BassAudioSample.cs` |
| 11 | High | H5: Replace `while(!www.isDone);` busy-wait with `await www.SendWebRequest().ToUniTask(...)` | `Assets/Scripts/IO/Base/Audio/UnityAudioSample.cs` |

(Note: H2+H3+H7 are commits 8, 9, 10 — three commits in same file but cleanly separable.)

### Group E — Concurrency (1 commit)

| # | Severity | Title | File(s) |
|---|----------|-------|---------|
| 12 | High | H8: Replace `Dictionary`s in `Online.cs` with `ConcurrentDictionary` (and remove dead `SpinLock`s) | `Assets/Scripts/Misc/Net/Online.cs` |

### Group F — Persistence and stripping (2 commits)

| # | Severity | Title | File(s) |
|---|----------|-------|---------|
| 13 | Critical | C4: Atomic settings/runtime writes via `tmp + File.Replace` under a lock | `Assets/Scripts/Misc/Base/MajEnv.cs` |
| 14 | Critical | C5: Expand `link.xml` to preserve serialized DTOs against IL2CPP stripping | `Assets/link.xml` |

### Group G — Recorder (1 commit)

| # | Severity | Title | File(s) |
|---|----------|-------|---------|
| 15 | High | H10: Make `OBSRecorder` cancellable, mark cross-thread state `volatile`, guard `File.Move` | `Assets/Scripts/Misc/Recording/OBSRecorder.cs` |

**Total: 15 commits across ~8 files.**

---

## 2. Per-Fix Contracts

For each fix below: **Symptom** (what's wrong), **Contract** (what must be true after), **Risk** (what could break).

### Commit 1 — C1: DisplayOffset double-application

**Symptom**: `GamePlayManager.ParseChart` calls `_chart.AddOffset(-_displayOffsetSec)`, and `NoteDrop` also adds `DisplayOffset` into `USERSETTING_JUDGE_OFFSET_SEC`. Effective drift = `2 × DisplayOffset` between visuals and judgement.

**Contract**:

- After fix, the *effective* judge timing for any note equals `chart_raw_time + JudgeOffset` (DisplayOffset participates only in visual placement, not in judgement math).
- **Decision procedure**: before editing, read both call sites and trace whether `_chart.AddOffset(...)` shifts only judge-time or also visual spawn-time. Whichever site does NOT govern visuals is the one to remove. Document the trace in the commit body.

**Risk**: visual placement could shift if we pick the wrong site to remove. Mitigation: read both call sites carefully, look for `transform.position` / spawn-time consumers of the chart, and pick the site that does NOT govern visuals.

### Commit 2 — H1: Touch JudgableRange off-by-1-frame

**Symptom**: `TouchDrop.JudgableRange` upper bound hardcoded as `+ 0.316667f` (19 frames), while `TOUCH_JUDGE_GOOD_AREA_MSEC = 18 * FRAME_LENGTH_MSEC` (18 frames).

**Contract**: upper bound is `JudgeTimingWithOffset + TOUCH_JUDGE_GOOD_AREA_MSEC / 1000f`. Both ends derived from the same constant.

**Risk**: if `0.316667f` was intentional (e.g. one frame buffer for input latency), we'll remove a deliberate forgiveness window. Mitigation: check git blame for an explanatory commit; if found, leave a comment and skip the fix. Otherwise, fix.

### Commit 3 — H9: Silent double-release in NotePool.Bucket.Return

**Symptom**: `if (_cursor <= 0) return;` swallows double-release; next `Rent` returns a still-live note.

**Contract**: in editor / dev builds, double-release logs `MajDebug.LogError` with stack info; in retail builds, increments a `_doubleReleaseCount` field exposed via `NotePoolManager` debug overlay.

**Risk**: low. Adding an error log can't cause new bugs (worst case spam if there's an existing latent double-release — which is precisely what we want to surface).

### Commit 4 — C3: NotePool/Bucket ArrayPool leak

**Symptom**: `NotePool.Dispose` reassigns field to `Array.Empty<>()` *before* returning to pool, returning the empty sentinel; `Bucket.Dispose` returns the unused `_rentedArray` field while `_storage` (the actual rental) leaks.

**Contract**: every `Pool<T>.RentArray(n)` is matched by exactly one `Pool<T>.ReturnArray(rented_array)` at Dispose, where `rented_array` is the original instance returned by `RentArray`.

**Risk**: low — the fix is local and exactly reverts to the obvious pattern. The Bucket fix may surface a hidden assumption that `_rentedArray` was meant to be a separate buffer; investigate before deleting it.

### Commit 5 — C2: Inverted CompareExchange in 3 Init paths

**Symptom**: `if (Interlocked.CompareExchange(ref _isInited, 0, 1) == 1) return;` — guards against the wrong case, guarantees double-init.

**Contract**: every `Init()` of TouchPanel / ButtonRing / LedDevice atomically transitions `_isInited` from 0 → 1 exactly once; subsequent calls return immediately. Pattern:

```csharp
if (Interlocked.CompareExchange(ref _isInited, 1, 0) != 0)
    return;
```

**Risk**: if some downstream code relies on Init re-running (it shouldn't), we'd suppress that. Read each Init to confirm idempotence is the intent.

### Commit 6 — H4: HID/Serial blocking calls ignore cancellation

**Symptom**: `while(true) { token.ThrowIfCancellationRequested(); hidStream.Write(...); }` — token only checked between iterations; if `Write` blocks indefinitely, Unity editor stop hangs.

**Contract**: each blocking IO loop registers `token.Register(() => stream.Dispose())` once before the loop. On cancellation, the dispose unblocks the call with `ObjectDisposedException`, which the existing `catch` handles. Loop also sets `ReadTimeout`/`WriteTimeout` to bounded values (e.g. 1000ms) where the API allows.

**Risk**: registering a cancellation callback that holds a reference to the stream extends lifetime. Mitigation: dispose the `CancellationTokenRegistration` in `finally` to break the reference cycle.

### Commit 7 — H6: AudioManager.Init half-state on exception

**Symptom**: `_isInited = true` is set before Bass.Init / WASAPI init runs; if those throw, the manager looks ready but isn't.

**Contract**: `_isInited = true` is the LAST statement inside the lock, only reached after every Bass/WASAPI/MixingMatrix init call returned without throwing. On exception, no rollback needed beyond what the throwing API itself cleans up (we will document any half-state risks in a comment).

**Risk**: callers that depended on the buggy timing (e.g. self-awakening on partial init) would break. Search for `_isInited` reads in same file to confirm.

### Commit 8 — H2: BassAudioSample finalizer post-Bass.Free crashes

**Symptom**: `~BassAudioSample() => Dispose()` runs on GC finalizer thread; if `AudioManager.OnDestroy` already called `Bass.Free()`, the finalizer's calls into freed Bass state cause native AV.

**Contract**: remove the finalizer entirely. `BassAudioSample` is fully `IDisposable`-driven; callers must dispose. If a caller is found that doesn't dispose, fix that caller in the same commit (or document as a follow-up issue if non-trivial).

**Risk**: any caller that relied on GC-driven cleanup will leak. Search for `new BassAudioSample` and ensure all consumers dispose properly (likely already do via `using` or explicit `Dispose`).

### Commit 9 — H3: BassAudioSample GCHandle leak

**Symptom**: pinned `GCHandle` passed into `Bass.CreateStream(byte[] memory)` is never stored in the object, so `Dispose` cannot free it.

**Contract**: leaf constructor allocates `_dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned)` and stores it on the instance. `Dispose` calls `if (_dataHandle.IsAllocated) _dataHandle.Free();` after Bass stream is freed.

**Risk**: the existing constructor chain delegates through several overloads — must walk it carefully and ensure the pin/handle lives exactly as long as Bass needs it.

### Commit 10 — H7: BassAudioSample normalization divide-by-zero

**Symptom**: `var scale = short.MaxValue / (float)channelmax;` produces `Infinity` / `NaN` when input is silent or decode failed; multiplied into buffer → Bass plays garbage at max volume (hearing-loss risk on headphones).

**Contract**: `if (channelmax <= 0) { /* skip normalization, log warning */ return; }` immediately before the scale computation.

**Risk**: silent normalize path must skip ALL downstream multiplication — verify there's no later code that assumes `scale` was assigned.

### Commit 11 — H5: UnityAudioSample busy-wait

**Symptom**: `while (!www.isDone) ;` — 100% CPU on a core during audio clip load.

**Contract**: replace with `await www.SendWebRequest().ToUniTask(cancellationToken: token);`. If the enclosing method is currently sync, change its signature to async — but only if all callers can be updated within this commit. If signature change spreads, restrict to using `.ToUniTask().GetAwaiter().GetResult()` *temporarily* with a TODO referencing this issue.

**Risk**: signature change cascade. Mitigation: prefer making the chain async if scope is small; otherwise scoped synchronous-await with TODO is acceptable.

### Commit 12 — H8: Online.cs Dictionary thread safety

**Symptom**: `static SpinLock` declared but never used. `Dictionary<,>` accessed from multiple threads via async/`UniTask.SwitchToThreadPool`. .NET dict-corruption-under-concurrent-write is a known heisenbug.

**Contract**: replace the two `Dictionary` fields with `ConcurrentDictionary<,>` (matching API). Delete the dead `SpinLock` declarations.

**Risk**: minor API surface differences (e.g. no indexer-assign atomicity). Walk every read/write site and ensure semantics preserved; specifically `Add` → `TryAdd`, indexer assignment → `AddOrUpdate`.

### Commit 13 — C4: Atomic settings writes

**Symptom**: `File.WriteAllText` truncates then writes; crash mid-write → 0-byte settings.json → `Settings = new()` silently → all user config lost.

**Contract**:

- `OnSave` acquires a `SemaphoreSlim` (private static, lazy-init).
- For each of `SettingsPath` and `_runtimeConfigPath`:
  1. Serialize to string `json`.
  2. Write to `path + ".tmp"`.
  3. `File.Replace(path + ".tmp", path, path + ".bak")` — atomic on NTFS/APFS.
  4. On Android, fallback to: write to `.tmp` + `fsync` + `File.Move(.tmp, path)` (delete existing `.bak` first; rename existing to `.bak`).
- Any IO exception logs but does not throw out of `OnSave`.
- Existing `.bak` recovery path on read is preserved; we additionally protect against zero-byte JSON by treating empty/short content as "missing, use defaults" (without overwriting `.bak`).

**Risk**: cross-platform file replace semantics. Mitigation: detect Android via `Application.platform` and use the safe fallback there.

### Commit 14 — C5: link.xml preserve serialized DTOs

**Symptom**: `link.xml` only preserves `MajdataPlay.Settings*`. Newtonsoft.Json reflects over `JudgeInfo`, `JudgeDetail`, score record types, `MajdataPlay.Net.*`, `RuntimeConfig`, `MachineInfo`, `ApiEndpoint`, etc. IL2CPP medium/high stripping deletes these → mobile builds crash on first deserialization.

**Contract**: enumerate every type referenced by `JsonConvert.DeserializeObject<T>` / `JsonSerializer.Deserialize<T>` / `[JsonConverter(...)]` / loaded from `.json` files. Each becomes a `<type fullname="..." preserve="all"/>` entry. Specifically must include (at minimum):

- `MajdataPlay.Scenes.Game.JudgeInfo*`
- `MajdataPlay.Scenes.Game.JudgeDetail*`
- `MajdataPlay.Net.*` (Online.cs DTOs)
- `MajdataPlay.Collections.*` score record types
- `MajdataPlay.Settings.*` (already present) + `MajdataPlay.Settings.Runtime.*`
- Newtonsoft.Json's `JsonConverter` derivatives in this assembly (e.g. `JudgeInfoConverter`)

Verification: post-fix, run `git grep -l 'JsonConvert\.'` and `git grep -l '\[JsonConverter'` and confirm every owning namespace is in `link.xml`.

**Risk**: over-preserving wastes binary size (negligible at this scale). Under-preserving is the bug. Err on the side of preserving entire namespaces with wildcards.

### Commit 15 — H10: OBSRecorder lifecycle

**Symptom**: infinite reconnect loop with no token, NRE risk on `_webSocket = null`, cross-thread `bool` reads not synchronized, `File.Move` reads `_obsOutPath` set by a different event branch with no ordering guarantee.

**Contract**:

- `StartRecordAsync` takes a `CancellationToken` (default: `CancellationToken.None`), bounded retry (e.g. 30 attempts × 1s) before throwing.
- `_isConnected` and `_isRecording` declared `volatile` (or wrapped via `Interlocked` helpers).
- `Dispose` checks-and-clears `_webSocket` under a lock; callbacks check for null before each use.
- `File.Move` is wrapped in `try/catch (FileNotFoundException)` and skipped if `_obsOutPath` was never set; logs warning.

**Risk**: API surface changes (token added) — but defaulted, so callers compile unchanged.

---

## 3. Verification Strategy

We cannot run Unity locally on this machine. Verification is:

1. **Per-fix static review**: re-read the file after edit; confirm the bug is gone by checking the contract above; check for new compile errors via syntax (no actual `csc`).
2. **Cross-file impact search**: for each modified symbol, `rg` to find all readers; sanity check no caller broke.
3. **Per-fix commit**: bisect-friendly. If a build later fails on Unity, the offending commit is identifiable.
4. **Final pass**: `git diff upstream/dev fix/critical-and-high` reviewed as a whole.
5. **Codex review** at the end as the last verification line.

We accept that **runtime verification on Unity Editor is out of scope** for this session. The user (or upstream maintainers) must run a build before merging to upstream.

---

## 4. Commit Message Template

```
<group>: <one-line summary> (<severity ID>)

Symptom: <one sentence what was wrong>
Why: <one sentence why it matters>
Fix: <one sentence what changed>
Evidence: <file:line of original bug>
Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md
```

Example:
```
io: fix inverted Interlocked.CompareExchange in 3 Init paths (C2)

Symptom: every Init() of TouchPanel/ButtonRing/LedDevice ran twice because
  the guard checked CompareExchange(ref _isInited, 0, 1) instead of (1, 0).
Why: doubled background threads, contention, and corrupted "is initialized"
  semantics; downstream code believed init was complete.
Fix: invert to CompareExchange(ref _isInited, 1, 0) != 0 → return; pattern.
Evidence: Assets/Scripts/IO/InputManager/InputManager.TouchPanel.cs L52
Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md
```

---

## 5. Out of Scope (Explicit)

- Medium and Low issues (M1–M17, L1–L8 from review).
- Adding test infrastructure (`*.Tests.asmdef`).
- Refactoring meme constants (`114514`, `1919810`).
- CI fixes (`.github/workflows/main.yml` casing bug, build-vs-test ordering).
- Editor build pipeline fixes (`BuildProcessor.cs` try/finally, `AndroidProcessor.cs` write timing).
- Submodule updates.
- License compliance audit (LGPL VLC etc.).

These belong in subsequent branches.

---

## 6. Decision Log

- **Branching**: single branch, multi-commit (user choice A on 2026-05-27).
- **Scope**: 5 Critical + 10 High (user choice B on 2026-05-27).
- **Verification**: static-only this session; codex as final review.
- **No PR upstream this pass**: push to fork, user decides about upstream PR later.

---

*End of design.*
