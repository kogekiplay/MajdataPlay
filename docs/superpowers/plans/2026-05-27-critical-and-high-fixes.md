# Critical and High Severity Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply 15 targeted bug fixes (5 Critical + 10 High) identified in the 2026-05-27 review, one commit per fix, on branch `fix/critical-and-high` of the `kogekiplay/MajdataPlay` fork.

**Architecture:** Pure bugfix branch. No new features, no API changes, no refactoring beyond what each fix requires. Each task is self-contained and bisectable. Verification is static (no Unity build environment available in this session); codex review serves as the final pass.

**Tech Stack:** Unity 2022.3.62f3, C# 11 (preview), UniTask, ManagedBass, HidSharp, Newtonsoft.Json, IL2CPP.

**Spec:** `docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md`

**Working directory:** `C:\Users\kogeki\dev\MajdataPlay` (use `/c/Users/kogeki/dev/MajdataPlay` from bash).

---

## TDD Adaptation Notice

The project has **no automated test infrastructure** and we **cannot run Unity Editor** in this session. We adapt TDD as follows:

- "Failing test" → `rg` command that *evidences the bug at current HEAD* (proves the bug exists)
- "Run test, observe fail" → show the buggy code excerpt
- "Implementation" → the edit
- "Run test, observe pass" → re-grep / re-read confirming the fix pattern is present and the bug pattern is gone
- "Commit" → small atomic commit per fix

This is the strongest verification we can give without a build environment. Codex review at the end is the runtime-knowledge backstop.

**Per-task constraint:** if Step 1 (grep) does NOT find the buggy pattern at the cited path, STOP and report — the code may have moved or already been fixed since the review.

---

## Task 1: C1 — Stop applying DisplayOffset twice in note timing

**Severity:** Critical
**Files:**
- Investigate: `Assets/Scripts/Scenes/Game/GamePlayManager.cs`
- Investigate: `Assets/Scripts/Scenes/Game/NoteBehaviours/NoteDrop.cs`
- Modify: one of the above (decision in Step 2)

- [ ] **Step 1: Confirm both offset sites exist at HEAD**

Run:
```bash
rg -n 'AddOffset|_displayOffsetSec|DisplayOffset' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/GamePlayManager.cs /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/NoteBehaviours/NoteDrop.cs
```
Expected: matches showing `_chart = _chart.AddOffset(-_displayOffsetSec)` in `GamePlayManager.cs` AND `(JudgeOffset + DisplayOffset) * unit` (or equivalent) in `NoteDrop.cs`.

If either does NOT match, STOP — code has moved. Search the whole `Scenes/Game/` tree with `rg -n 'AddOffset' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/ -g '*.cs'` to relocate, and update task before proceeding.

- [ ] **Step 2: Trace which site governs VISUAL placement vs JUDGEMENT**

Read both files with adequate context (200-line window around each match). The decision rule:

- If `_chart.AddOffset(...)` shifts a chart whose timestamps drive **only** note-spawn / visual placement (not judgement math), then JUDGEMENT shouldn't be doing `+ DisplayOffset` again → **fix NoteDrop**.
- If `_chart.AddOffset(...)` shifts a chart whose timestamps drive **judgement** (note.judgeTime read directly), then visual offset is already in the chart and `NoteDrop` shouldn't add it again → **fix NoteDrop**.
- If `NoteDrop`'s `DisplayOffset` is genuinely needed for visual positioning (e.g., to shift the SPRITE position by a fixed visual offset distinct from the chart shift), then **fix GamePlayManager** by removing the `AddOffset` call.

Additional check: `rg -n 'judgeTime|JudgeTimingWithOffset|GetJudgeTime' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/ -g '*.cs'` to see who reads the chart timings.

Record the trace conclusion in a scratch note for the commit message.

- [ ] **Step 3: Apply the fix (remove the duplicate offset application)**

Based on Step 2 decision, edit ONE of:
- `GamePlayManager.cs`: remove the `_chart = _chart.AddOffset(-_displayOffsetSec);` line (or change to a no-op shift), OR
- `NoteDrop.cs`: change `(JudgeOffset + DisplayOffset) * unit` → `JudgeOffset * unit` and update any nearby formulas accordingly.

Do NOT change both. Do NOT introduce a feature flag.

- [ ] **Step 4: Confirm fix is applied, original buggy pattern is gone**

Run:
```bash
rg -n 'JudgeOffset \+ DisplayOffset|JudgeOffset\s*\+\s*DisplayOffset' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/ -g '*.cs'
rg -n 'AddOffset\(-_displayOffsetSec\)' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/ -g '*.cs'
```
Expected: exactly ONE of these patterns matches (the one you kept); the other returns no results.

Cross-check: `rg -n 'DisplayOffset' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/ -g '*.cs'` — confirm remaining usages are only for visual placement (not judgement math).

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
game: stop applying DisplayOffset twice in note timing (C1)

Symptom: chart timestamps were pre-shifted by -_displayOffsetSec in
  GamePlayManager.ParseChart, AND NoteDrop added DisplayOffset again into
  USERSETTING_JUDGE_OFFSET_SEC. Net drift between visuals and judgement
  was 2 * DisplayOffset, encouraging users to compensate via JudgeOffset
  and masking the bug.
Why: violates the core promise of frame-accurate judgement; every player
  using a non-zero DisplayOffset was silently miscalibrated.
Fix: <one-line: "removed redundant AddOffset in GamePlayManager" OR
  "removed DisplayOffset from NoteDrop judge formula"> — see body for
  trace of why this site was chosen.

Trace: <2-3 lines from Step 2 documenting which site governs visuals
  vs judgement, and why we removed the other>.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (C1)
EOF
)"
```

---

## Task 2: H1 — Derive Touch JudgableRange upper bound from constant

**Severity:** High
**Files:**
- Modify: `Assets/Scripts/Scenes/Game/NoteBehaviours/TouchDrop.cs`

- [ ] **Step 1: Confirm the hardcoded `+ 0.316667f` exists**

Run:
```bash
rg -n '0\.316667|0\.15f|TOUCH_JUDGE_GOOD_AREA_MSEC|FRAME_LENGTH_MSEC' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/NoteBehaviours/TouchDrop.cs
```
Expected: match showing `JudgableRange = new(JudgeTimingWithOffset - 0.15f, JudgeTimingWithOffset + 0.316667f, ...)` and a `TOUCH_JUDGE_GOOD_AREA_MSEC = 18 * FRAME_LENGTH_MSEC` constant nearby.

If the hardcoded number is different (e.g. has been adjusted), STOP — re-verify the off-by-one before changing.

- [ ] **Step 2: Read surrounding context, check git blame**

Read 60 lines around the match. Run:
```bash
git -C /c/Users/kogeki/dev/MajdataPlay log -5 --pretty=oneline -- Assets/Scripts/Scenes/Game/NoteBehaviours/TouchDrop.cs
git -C /c/Users/kogeki/dev/MajdataPlay blame -L /JudgableRange/,+5 Assets/Scripts/Scenes/Game/NoteBehaviours/TouchDrop.cs
```

If the blame commit explicitly says "extra frame for input latency" or similar, STOP — the value was deliberate; document and skip the fix.

- [ ] **Step 3: Replace the hardcoded upper bound with the constant**

Edit `TouchDrop.cs`:

```csharp
// Before:
JudgableRange = new(JudgeTimingWithOffset - 0.15f,
                    JudgeTimingWithOffset + 0.316667f, ContainsType.Closed);

// After (also align lower bound for symmetry — 0.15f equals 9 * FRAME_LENGTH_MSEC / 1000f, name it):
JudgableRange = new(JudgeTimingWithOffset - TOUCH_JUDGE_GOOD_AREA_MSEC / 1000f / 2f,
                    JudgeTimingWithOffset + TOUCH_JUDGE_GOOD_AREA_MSEC / 1000f, ContainsType.Closed);
```

CAUTION: only change the UPPER bound if the design rule is "Good window on one side". If both bounds are symmetric Good windows, change both to the same `TOUCH_JUDGE_GOOD_AREA_MSEC / 1000f` expression. Read the file's other note types (`HoldDrop`, `TapDrop`) to confirm the convention before editing.

- [ ] **Step 4: Verify**

```bash
rg -n '0\.316667' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/NoteBehaviours/TouchDrop.cs
```
Expected: no matches.

```bash
rg -n 'JudgableRange' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/NoteBehaviours/TouchDrop.cs
```
Expected: the new expression uses `TOUCH_JUDGE_GOOD_AREA_MSEC`.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
game: derive Touch JudgableRange from TOUCH_JUDGE_GOOD_AREA_MSEC (H1)

Symptom: TouchDrop.JudgableRange upper bound was hardcoded as +0.316667f
  (~19 frames at FRAME_LENGTH_MSEC=16.6667ms), but TOUCH_JUDGE_GOOD_AREA_MSEC
  = 18 * FRAME_LENGTH_MSEC defines an 18-frame Good window. Late-side
  touches in the 300-317ms band were judged when they should have been
  ignored as Miss.
Why: occasional unexpected late Good results on touch notes; inconsistency
  with the constant the file itself declares.
Fix: derive the bound from TOUCH_JUDGE_GOOD_AREA_MSEC so future constant
  changes propagate.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (H1)
EOF
)"
```

---

## Task 3: H9 — Log/assert on NotePool.Bucket double-release

**Severity:** High
**Files:**
- Modify: `Assets/Scripts/Scenes/Game/Buffers/NotePool.cs`

- [ ] **Step 1: Confirm the silent guard exists**

Run:
```bash
rg -n 'class Bucket|public void Return|_cursor' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/Buffers/NotePool.cs
```
Expected: matches showing `public void Return(...)` with `if (_cursor <= 0) return;`.

- [ ] **Step 2: Read 50 lines around the Bucket class**

Note the existing logging helpers used elsewhere in the file (look for `MajDebug.LogError`, `MajDebug.LogWarning`, or `Debug.LogError`).

- [ ] **Step 3: Replace the silent return with a logged early return**

Edit `NotePool.cs` — replace:
```csharp
public void Return(NoteDrop note) {
    if (_cursor <= 0) return;
    _storage[--_cursor] = note;
}
```

with:
```csharp
public void Return(NoteDrop note) {
    if (_cursor <= 0)
    {
        MajDebug.LogError($"NotePool.Bucket.Return: double-release detected for note {note?.GetType().Name ?? "null"}; bucket already empty.");
        return;
    }
    _storage[--_cursor] = note;
}
```

If `MajDebug` is not in scope here, add the appropriate `using` (search `rg -n 'using MajdataPlay' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/Buffers/ -g '*.cs'` for the convention).

Choose the exact symbol name (`MajDebug`, `Debug`, or whatever the project uses) by checking what already gets called elsewhere in `NotePool.cs`.

- [ ] **Step 4: Verify**

```bash
rg -n 'double-release detected' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/Buffers/NotePool.cs
```
Expected: one match.

```bash
rg -n 'if \(_cursor <= 0\) return;' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/Buffers/NotePool.cs
```
Expected: no matches (the silent return is gone).

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
game: log on NotePool.Bucket double-release (H9)

Symptom: Bucket.Return silently swallowed double-release; the next Rent
  then returned a stale instance still attached to the live note, causing
  occasional "ghost note" visuals — virtually impossible to reproduce.
Why: surfacing the bug lets us actually find it; the cost is one log line
  per occurrence on a path that should fire zero times.
Fix: replace silent early-return with MajDebug.LogError that names the
  offending type, then keep the early-return to preserve current behavior
  on production builds.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (H9)
EOF
)"
```

---

## Task 4: C3 — Stop leaking ArrayPool rentals in NotePool/Bucket Dispose

**Severity:** Critical
**Files:**
- Modify: `Assets/Scripts/Scenes/Game/Buffers/NotePool.cs`

- [ ] **Step 1: Confirm both leaks exist at HEAD**

Run:
```bash
rg -n 'ReturnArray|RentArray|_rentedArray|_storage|Array\.Empty' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/Buffers/NotePool.cs
```
Expected:
- Outer `NotePool.Dispose` (or equivalent): assigns field to `Array.Empty<...>()` then passes the same field to `Pool<...>.ReturnArray(...)`.
- Inner `Bucket`: declares `_rentedArray = Array.Empty<TPoolingInfo>()` separately from `_storage`, constructs `_storage = Pool<...>.RentArray(capacity)`, but `Dispose` calls `Pool<...>.ReturnArray(_rentedArray)`.

If either pattern is absent or different, STOP — re-read and adjust the plan.

- [ ] **Step 2: Read the full NotePool.cs file to understand pool semantics**

Run:
```bash
wc -l /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/Buffers/NotePool.cs
```

Read the whole file. Confirm:
- `Pool<T>.ReturnArray(arr, clear)` is the project's wrapper; behaves like `ArrayPool<T>.Shared.Return(arr, clearArray: clear)`.
- The intent at Dispose is to return ALL rented buffers so the pool can reuse them.

- [ ] **Step 3: Fix `NotePool.Dispose` — capture local before reassign**

Edit `NotePool.cs`:

```csharp
// Before:
_rentedArrayForTimingPoints = Array.Empty<TimingPoint<TInfo>>();
Pool<TimingPoint<TInfo>>.ReturnArray(_rentedArrayForTimingPoints, true);
// (same pattern for _rentedArrayForNotePoolingInfos)

// After:
var rentedTimingPoints = _rentedArrayForTimingPoints;
_rentedArrayForTimingPoints = Array.Empty<TimingPoint<TInfo>>();
Pool<TimingPoint<TInfo>>.ReturnArray(rentedTimingPoints, true);
// (same shape for the second field)
```

Apply the same capture-local-then-reassign pattern to every leaked rental in `NotePool.Dispose`.

- [ ] **Step 4: Fix `Bucket.Dispose` — return the actually-rented storage**

Edit `Bucket` class inside `NotePool.cs`:

```csharp
// Before:
class Bucket {
    TPoolingInfo[] _rentedArray = Array.Empty<TPoolingInfo>();
    TPoolingInfo[] _storage;
    public Bucket(int capacity) { _storage = Pool<TPoolingInfo>.RentArray(capacity); }
    public void Dispose() { Pool<TPoolingInfo>.ReturnArray(_rentedArray); }
}

// After (simplest fix: drop the unused field):
class Bucket {
    TPoolingInfo[] _storage;
    public Bucket(int capacity) { _storage = Pool<TPoolingInfo>.RentArray(capacity); }
    public void Dispose()
    {
        var rented = _storage;
        _storage = Array.Empty<TPoolingInfo>();
        Pool<TPoolingInfo>.ReturnArray(rented);
    }
}
```

If `_rentedArray` is actually read elsewhere (search `rg -n '_rentedArray' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/Buffers/NotePool.cs`), use the alternative fix: `_rentedArray = _storage;` in the constructor instead of deleting the field.

- [ ] **Step 5: Verify all rentals are matched**

```bash
rg -n 'RentArray|ReturnArray' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/Buffers/NotePool.cs
```

Confirm every `RentArray` call has a corresponding `ReturnArray` of the same captured reference.

```bash
rg -n 'Array\.Empty.*ReturnArray|ReturnArray.*Array\.Empty' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Scenes/Game/Buffers/NotePool.cs
```
Expected: no matches (no line both wipes the field AND returns it).

- [ ] **Step 6: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
game: stop leaking ArrayPool rentals in NotePool/Bucket Dispose (C3)

Symptom: NotePool.Dispose reassigned _rentedArray* fields to
  Array.Empty<>() BEFORE passing them to Pool<T>.ReturnArray, so empty
  sentinels were returned and actual rentals leaked. Bucket.Dispose
  returned a never-assigned field instead of the _storage rental.
  Practice-mode replays (frequent scene reloads) grew ArrayPool buckets
  without bound, eventually OOMing on Android/Quest builds.
Why: long-session memory growth = silent ship-blocker on memory-tight
  mobile devices.
Fix: capture rentals into locals before nulling the fields; return the
  captured references. In Bucket, drop the unused _rentedArray field and
  return _storage directly.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (C3)
EOF
)"
```

---

## Task 5: C2 — Fix inverted Interlocked.CompareExchange in 3 Init paths

**Severity:** Critical
**Files:**
- Modify: `Assets/Scripts/IO/InputManager/InputManager.TouchPanel.cs`
- Modify: `Assets/Scripts/IO/InputManager/InputManager.ButtonRing.cs`
- Modify: `Assets/Scripts/IO/OutputManager/RawDeviceHandle/OutputManager.LedDevice.cs`

- [ ] **Step 1: Confirm all three inverted guards exist**

```bash
rg -n 'Interlocked\.CompareExchange\(ref _isInited, 0, 1\)' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/ -g '*.cs'
```
Expected: 3 matches across the three files above.

If only some match (perhaps one was already fixed), proceed with just those that still have the bug; note the variance in the commit message.

If the variable name differs (e.g. `_initialized`), update the grep accordingly.

- [ ] **Step 2: Read each file's Init method**

For each of the three files, locate and read the `Init` (or equivalent entry point) method including the guard line and the next 30 lines so you understand what the second-init path would corrupt.

- [ ] **Step 3: Invert each guard**

For each of the three files, replace:
```csharp
if (Interlocked.CompareExchange(ref _isInited, 0, 1) == 1)
{
    return;
}
```

with:
```csharp
if (Interlocked.CompareExchange(ref _isInited, 1, 0) != 0)
{
    return;
}
```

Make the edit identical in all three files. Preserve surrounding whitespace and braces exactly.

- [ ] **Step 4: Verify all three are fixed**

```bash
rg -n 'Interlocked\.CompareExchange\(ref _isInited, 0, 1\)' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/ -g '*.cs'
```
Expected: no matches.

```bash
rg -n 'Interlocked\.CompareExchange\(ref _isInited, 1, 0\)' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/ -g '*.cs'
```
Expected: 3 matches (same files as Step 1).

Bonus: `rg -n 'CompareExchange' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/ -g '*.cs'` — confirm no other `(ref X, 0, 1)` patterns lurk elsewhere.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
io: fix inverted Interlocked.CompareExchange in 3 Init paths (C2)

Symptom: TouchPanel/ButtonRing/LedDevice Init() guards were written as
  CompareExchange(ref _isInited, 0, 1) == 1 — which never sets the flag
  and never blocks. Every Init() ran the body, spawning a second
  LongRunning background thread per device per call.
Why: corrupted "is initialized" semantics, duplicated polling threads,
  unbounded thread growth on repeated Init calls (e.g. scene reload).
Fix: invert to the canonical CAS guard:
    if (Interlocked.CompareExchange(ref _isInited, 1, 0) != 0) return;
  so the first call atomically wins and subsequent calls short-circuit.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (C2)
EOF
)"
```

---

## Task 6: H4 — Make HID/Serial blocking IO cancellable

**Severity:** High
**Files:**
- Modify: `Assets/Scripts/IO/InputManager/InputManager.ButtonRing.cs`
- Modify: `Assets/Scripts/IO/OutputManager/RawDeviceHandle/OutputManager.LedDevice.cs`
- Modify: `Assets/Scripts/IO/InputManager/InputManager.TouchPanel.cs` (only if blocking IO loops exist there too)

- [ ] **Step 1: Find blocking IO loops with non-cancellable Read/Write**

```bash
rg -n 'hidStream\.(Read|Write)|HidStream' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/ -g '*.cs'
rg -n 'SerialPort.*\.(Read|Write|ReadByte|ReadLine)' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/ -g '*.cs'
```

For each match, read the enclosing loop. The bug pattern:
```csharp
while (true)
{
    token.ThrowIfCancellationRequested();   // ← only checked between iterations
    hidStream.Write(reportBuffer);          // ← blocks indefinitely on disconnected device
}
```

- [ ] **Step 2: For each affected loop, capture token and stream identifiers**

You will register `token.Register(() => stream.Dispose())` once before the loop. On cancellation, the dispose call unblocks the in-flight Read/Write with `ObjectDisposedException`, which the existing catch handles.

- [ ] **Step 3: Apply the cancellation registration pattern**

For each loop, transform from:

```csharp
// Before:
using var hidStream = device.Open();
while (true)
{
    token.ThrowIfCancellationRequested();
    // ... hidStream.Read/Write ...
}
```

to:

```csharp
// After:
using var hidStream = device.Open();
hidStream.ReadTimeout = 1000;   // bounded; some HidStream impls honor it, some don't — defense in depth
hidStream.WriteTimeout = 1000;
using var cancelReg = token.Register(static state => ((IDisposable)state!).Dispose(), hidStream);
try
{
    while (true)
    {
        token.ThrowIfCancellationRequested();
        // ... hidStream.Read/Write ...
    }
}
catch (ObjectDisposedException) when (token.IsCancellationRequested)
{
    // expected on cancellation
}
```

For `SerialPort`, use the same pattern but `serial.Dispose()` instead of `hidStream.Dispose()`.

If the surrounding code already has a try/catch that covers `ObjectDisposedException`, do not add a second one — just add the `using var cancelReg = ...` line and the `ReadTimeout`/`WriteTimeout` settings. Note: ReadTimeout/WriteTimeout property setters may throw on streams that don't support them — wrap each in `try { ... } catch (InvalidOperationException) { /* property not supported */ } catch (IOException) { }`.

- [ ] **Step 4: Verify all blocking IO loops now have a cancellation registration**

```bash
rg -n 'token\.Register|cancellationToken\.Register' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/ -g '*.cs'
```
Expected: at least one match per modified file.

```bash
rg -B 2 -A 10 'hidStream\.(Read|Write)' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/ -g '*.cs' | rg -B 5 'while \(true\)|while\(true\)'
```
Manually inspect: every `while(true)` enclosing an HID Read/Write should have a `token.Register(...)` upstream.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
io: make HID/Serial blocking IO cancellable (H4)

Symptom: while(true) { token.ThrowIfCancellationRequested(); hidStream.Write(...); }
  only checks the token between iterations. If a device stops responding,
  Write blocks indefinitely; Unity editor Stop hangs, application Quit hangs.
Why: developer workflow breakage and Player-side process leaks on cabinets
  with flaky USB.
Fix: register token.Register(() => stream.Dispose()) once before each
  loop; ObjectDisposedException unblocks the in-flight call and is
  swallowed by the existing catch. Also set ReadTimeout/WriteTimeout where
  the stream implementation supports it, as defense in depth.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (H4)
EOF
)"
```

---

## Task 7: H6 — Set AudioManager._isInited = true only after Init succeeds

**Severity:** High
**Files:**
- Modify: `Assets/Scripts/IO/AudioManager.cs`

- [ ] **Step 1: Confirm the premature-set pattern**

```bash
rg -n '_isInited|Init\(' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/AudioManager.cs
```

Expected: a method (likely `Init` or called from `Awake`) where `_isInited = true;` is set BEFORE Bass.Init / BassWasapi.Init / MixingMatrix allocation.

- [ ] **Step 2: Read 60 lines around the Init method**

Identify ALL initialization steps that could throw (Bass.Init, BassWasapi.Init, mixing matrix allocation, any other native call). These will be reordered.

- [ ] **Step 3: Move `_isInited = true` to the last statement inside the lock**

Edit `AudioManager.cs`:

```csharp
// Before:
lock (_initLock)
{
    if (_isInited) return;
    _isInited = true;
    Bass.Init(...);
    BassWasapi.Init(...);
    MixingMatrix = new float[channels, 2];
    // ... possibly more steps ...
}

// After:
lock (_initLock)
{
    if (_isInited) return;
    try
    {
        Bass.Init(...);
        BassWasapi.Init(...);
        MixingMatrix = new float[channels, 2];
        // ... possibly more steps ...
        _isInited = true;  // only set after every step succeeded
    }
    catch
    {
        // Best-effort cleanup of partially-initialized state
        try { BassWasapi.Stop(); } catch { }
        try { BassWasapi.Free(); } catch { }
        try { Bass.Free(); } catch { }
        throw;
    }
}
```

If the existing Init does not have a `try`, add one as shown. The catch block performs best-effort rollback of whatever native state may have been allocated, then rethrows so the caller knows Init failed.

If `Awake()` also calls Init (the review noted a possible second init entry point), confirm via `rg -n 'Init\(\)|Awake\(\)' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/AudioManager.cs` and verify both routes go through the same lock-protected method.

- [ ] **Step 4: Verify**

```bash
rg -B 2 -A 15 '_isInited = true' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/AudioManager.cs
```

Confirm `_isInited = true` is the last meaningful statement before exiting the try block; that any `throw` paths cannot leave the flag true.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
io/audio: set AudioManager._isInited only after Init succeeds (H6)

Symptom: _isInited = true was set BEFORE Bass.Init / BassWasapi.Init /
  MixingMatrix allocation. Any exception during native init left the
  manager in a half-built state but advertising itself as ready, so
  subsequent callers operated on undefined state.
Why: silent failure mode — Bass init throws once, AudioManager looks
  fine, every later audio call is undefined behavior.
Fix: wrap init steps in a try/catch that does best-effort rollback
  (BassWasapi.Stop/Free, Bass.Free) and rethrows; only set _isInited
  after the last successful step.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (H6)
EOF
)"
```

---

## Task 8: H2 — Remove BassAudioSample finalizer

**Severity:** High
**Files:**
- Modify: `Assets/Scripts/IO/Base/Audio/BassAudioSample.cs`

- [ ] **Step 1: Confirm the finalizer exists**

```bash
rg -n '~BassAudioSample|Dispose|IDisposable' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/BassAudioSample.cs
```

Expected: a finalizer `~BassAudioSample() => Dispose();` (or similar) and a `Dispose` method that calls Bass native APIs.

- [ ] **Step 2: Find all callers and verify they Dispose**

```bash
rg -n 'new BassAudioSample|BassAudioSample\.Create' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/ -g '*.cs'
```

For each caller, confirm there is a path that disposes the instance (`using`, explicit `.Dispose()` call, or stored in a field that gets disposed in a Dispose/OnDestroy method). Note any caller that does NOT — those are now leaks.

If any caller leaks, you have two options:
- (A) Fix that caller too in this commit (preferred if small).
- (B) Keep the finalizer but make it safe: wrap the body in `if (Bass.CurrentDevice >= 0) { ... }` — only call native if Bass is still alive — and use `GC.SuppressFinalize(this)` from `Dispose`.

Default decision: prefer option A (no finalizer) unless you find a caller that genuinely cannot be fixed in this commit. If option B, document in commit.

- [ ] **Step 3: Remove the finalizer (option A) or guard it (option B)**

**Option A (preferred):**
```csharp
// Remove this line entirely:
~BassAudioSample() => Dispose();

// And in Dispose(), add:
public void Dispose()
{
    // ... existing body ...
    GC.SuppressFinalize(this);   // harmless even without finalizer; keeps future finalizer-add safe
}
```

**Option B (only if A blocked):**
```csharp
~BassAudioSample()
{
    // Skip native cleanup if Bass has already been freed (e.g. on app shutdown).
    if (Bass.CurrentDevice < 0) return;
    Dispose();
}
```

Pick exactly one.

- [ ] **Step 4: Verify**

If option A:
```bash
rg -n '~BassAudioSample' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/BassAudioSample.cs
```
Expected: no matches.

```bash
rg -n 'GC\.SuppressFinalize' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/BassAudioSample.cs
```
Expected: one match (inside Dispose).

If option B: verify the guard pattern is present.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
io/audio: remove BassAudioSample finalizer to prevent post-Bass.Free crashes (H2)

Symptom: ~BassAudioSample() => Dispose() ran on the GC finalizer thread.
  If AudioManager.OnDestroy had already called Bass.Free(), the finalizer's
  subsequent calls into freed Bass state caused native access violations
  that surfaced as Unity crashes on exit.
Why: clean process shutdown is table stakes; crash-on-exit makes
  bug reports unreadable.
Fix: <"removed finalizer; rely on IDisposable contract" OR "guarded
  finalizer with Bass.CurrentDevice >= 0 check"> and added GC.SuppressFinalize
  in Dispose.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (H2)
EOF
)"
```

---

## Task 9: H3 — Track and free pinned GCHandle in BassAudioSample

**Severity:** High
**Files:**
- Modify: `Assets/Scripts/IO/Base/Audio/BassAudioSample.cs`

- [ ] **Step 1: Confirm the GCHandle leak path**

```bash
rg -n 'GCHandle|Bass\.CreateStream|StreamCreate.*byte\[\]|pinned|Pinned' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/BassAudioSample.cs
```

Expected: a constructor or `Create` method that pins a `byte[]` for Bass and passes the pointer in, with no `_dataHandle` field stored on the instance for later `Free()`.

- [ ] **Step 2: Read the constructor chain**

Identify:
- The "leaf" constructor that actually does the pinning.
- All public constructors that chain into it.
- The `Dispose` method.

Note any existing field that LOOKS like it should hold the handle but is `default` / unassigned.

- [ ] **Step 3: Store the pinned handle on the instance and free it in Dispose**

Edit `BassAudioSample.cs`:

```csharp
// Add field (if not present):
GCHandle _dataHandle;

// In the leaf constructor that owns the pinning:
public BassAudioSample(byte[] data, /*...other params...*/)
{
    _dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
    try
    {
        var ptr = _dataHandle.AddrOfPinnedObject();
        // ... existing Bass.CreateStream(ptr, length, ...) ...
    }
    catch
    {
        if (_dataHandle.IsAllocated) _dataHandle.Free();
        throw;
    }
}

// In Dispose, AFTER Bass stream cleanup:
public void Dispose()
{
    // ... existing Bass.StreamFree etc ...
    if (_dataHandle.IsAllocated) _dataHandle.Free();
    GC.SuppressFinalize(this);
}
```

CRITICAL ORDERING: the handle must be freed AFTER Bass has released the stream, not before. Bass may read the pinned data during `StreamFree`.

If the existing constructor chain passes `default` GCHandle through overloads (the review hinted at this), replace those default-passing overloads with ones that delegate to the leaf so pinning always happens at the same spot.

- [ ] **Step 4: Verify**

```bash
rg -n '_dataHandle' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/BassAudioSample.cs
```
Expected: declarations + alloc + free + IsAllocated check, all present.

```bash
rg -n 'GCHandle\.Alloc|_dataHandle\.Free' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/BassAudioSample.cs
```
Expected: at least one Alloc, at least one Free.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
io/audio: track and free pinned GCHandle in BassAudioSample (H3)

Symptom: BassAudioSample pinned a byte[] for Bass.CreateStream but never
  stored the GCHandle, so Dispose could not Free() it. Every loaded sample
  leaked one pinned managed array (potentially megabytes) for the rest of
  the process lifetime.
Why: long sessions, especially Practice mode reloads, accumulate pinned
  arrays that GC cannot relocate or reclaim.
Fix: store the GCHandle in a field at allocation site; free it in Dispose
  AFTER Bass.StreamFree (Bass may read the data during free).

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (H3)
EOF
)"
```

---

## Task 10: H7 — Guard against BassAudioSample normalization divide-by-zero

**Severity:** High
**Files:**
- Modify: `Assets/Scripts/IO/Base/Audio/BassAudioSample.cs`

- [ ] **Step 1: Confirm the divide-by-zero path**

```bash
rg -n 'short\.MaxValue|channelmax|scale|ChannelGetData' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/BassAudioSample.cs
```

Expected: a normalization loop computing `channelmax` from `Bass.ChannelGetData`, then `var scale = short.MaxValue / (float)channelmax;`, then a multiply loop scaling samples up.

- [ ] **Step 2: Read the full normalization function**

Confirm:
- `channelmax` starts at 0.
- It is updated only when a sample's absolute value exceeds the current max.
- Silent input / decode-error path produces `channelmax == 0`.

Identify the scale-and-multiply block that would consume `Infinity`/`NaN`.

- [ ] **Step 3: Add the guard before computing `scale`**

Edit `BassAudioSample.cs`:

```csharp
// Before:
var scale = short.MaxValue / (float)channelmax;
// ... multiply loop ...

// After:
if (channelmax <= 0)
{
    MajDebug.LogWarning("BassAudioSample: skipping normalization — channelmax is zero (silent input or decode error).");
    return; // skip normalization entirely; sample is silent or unreadable
}
var scale = short.MaxValue / (float)channelmax;
// ... multiply loop ...
```

`return` if the normalization function has nothing more to do after the multiply. If it does (e.g. cleanup), use a guarded conditional instead:

```csharp
if (channelmax > 0)
{
    var scale = short.MaxValue / (float)channelmax;
    // ... multiply loop ...
}
else
{
    MajDebug.LogWarning("BassAudioSample: skipping normalization — channelmax is zero.");
}
```

- [ ] **Step 4: Verify**

```bash
rg -B 1 -A 3 'short\.MaxValue / \(float\)channelmax' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/BassAudioSample.cs
```
Expected: the divide line is preceded by a `channelmax <= 0` (or `> 0`) check.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
io/audio: guard normalization against channelmax==0 (H7)

Symptom: BassAudioSample normalization computed
  scale = short.MaxValue / (float)channelmax
  without checking if channelmax was zero. Silent input or a decode error
  produced Infinity / NaN; multiplying into the buffer made Bass play
  garbage at full volume — a hearing-loss-class hazard on headphones.
Why: rhythm-game players typically wear headphones; an unexpected blast
  at max volume can cause genuine harm.
Fix: early-return (with warning log) when channelmax <= 0, leaving the
  sample untouched.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (H7)
EOF
)"
```

---

## Task 11: H5 — Replace UnityAudioSample busy-wait with UniTask

**Severity:** High
**Files:**
- Modify: `Assets/Scripts/IO/Base/Audio/UnityAudioSample.cs`
- Modify: any callers whose signature needs to become async (decided in Step 2)

- [ ] **Step 1: Confirm busy-wait**

```bash
rg -n 'while \(!www\.isDone\)|www\.SendWebRequest|UnityWebRequest' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/UnityAudioSample.cs
```

Expected: `var www = UnityWebRequestMultimedia.GetAudioClip(...); www.SendWebRequest(); while (!www.isDone) ;`

- [ ] **Step 2: Find all callers**

```bash
rg -n 'UnityAudioSample|UnityAudioSample\.Create' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/ -g '*.cs'
```

For the construction call site of UnityAudioSample, determine:
- Is the enclosing method already `async`? If yes, great — just await.
- If not, can the enclosing method easily become `async`? If yes, prefer async cascade.
- If the cascade would touch 5+ files or hit Unity lifecycle methods (Awake, Start, OnDestroy) that don't return Task, use `.GetAwaiter().GetResult()` with a TODO.

Default: prefer async cascade if it touches ≤2 caller files, otherwise sync await fallback.

- [ ] **Step 3a: If async cascade chosen — replace busy-wait with await**

Edit `UnityAudioSample.cs`:

```csharp
// Before:
public static UnityAudioSample Create(string uri, ...)
{
    var www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.UNKNOWN);
    www.SendWebRequest();
    while (!www.isDone) ;
    // ... build UnityAudioSample from www.downloadHandler ...
}

// After:
public static async UniTask<UnityAudioSample> CreateAsync(string uri, /*...,*/ CancellationToken token = default)
{
    using var www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.UNKNOWN);
    await www.SendWebRequest().ToUniTask(cancellationToken: token);
    // ... build UnityAudioSample from www.downloadHandler ...
}
```

Then update each caller to `await UnityAudioSample.CreateAsync(...);` and propagate async upwards.

- [ ] **Step 3b: If sync await fallback chosen**

Edit `UnityAudioSample.cs`:

```csharp
// Replace:
www.SendWebRequest();
while (!www.isDone) ;

// With:
// TODO(H5 follow-up): convert this and the call chain to async UniTask.
www.SendWebRequest().ToUniTask().GetAwaiter().GetResult();
```

CAUTION: this still blocks the main thread, just without burning CPU. It is strictly an interim improvement.

- [ ] **Step 4: Verify**

```bash
rg -n 'while \(!www\.isDone\)' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/ -g '*.cs'
```
Expected: no matches.

```bash
rg -n 'ToUniTask\(\)' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/IO/Base/Audio/UnityAudioSample.cs
```
Expected: at least one match.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
io/audio: replace UnityAudioSample busy-wait with UniTask await (H5)

Symptom: UnityAudioSample loading used while(!www.isDone); to wait for
  UnityWebRequest completion, pinning one CPU core during level load.
  On Android / low-end PCs this competed with the audio thread and
  caused load-time hitches.
Why: 100% CPU spin is never the right answer; UniTask already in project.
Fix: <"converted load path to async UniTask, propagated to N callers" OR
  "replaced spin with synchronous await via .GetAwaiter().GetResult() as
  interim; TODO to convert call chain to async">. No more busy spin.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (H5)
EOF
)"
```

---

## Task 12: H8 — Use ConcurrentDictionary in Online.cs

**Severity:** High
**Files:**
- Modify: `Assets/Scripts/Misc/Net/Online.cs`

- [ ] **Step 1: Confirm dead SpinLocks and unsafe Dictionary use**

```bash
rg -n 'SpinLock|_dictLock|_cachedResponseLock|Dictionary<' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Net/Online.cs
```

Expected: two `SpinLock` field declarations and two `Dictionary<,>` fields that are accessed from async/threaded contexts.

```bash
rg -n '_dictLock\.|_cachedResponseLock\.' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Net/Online.cs
```
Expected: no `Enter`/`Exit` calls (the locks are truly dead). If `Enter` IS called somewhere, the situation is different — STOP and reassess.

- [ ] **Step 2: Catalog every read/write of the two Dictionaries**

```bash
rg -n '_cachedResponse|_endpointStatistics' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Net/Online.cs
```

For each access, note the operation: index get, index set (assign), `Add`, `Remove`, `TryGetValue`, `ContainsKey`, enumeration. This determines the `ConcurrentDictionary` API mapping.

- [ ] **Step 3: Migrate to ConcurrentDictionary and delete SpinLocks**

Edit `Online.cs`:

```csharp
// Before:
static SpinLock _dictLock = new();
static SpinLock _cachedResponseLock = new();
readonly static Dictionary<OnlineSongDetail, CachedApiEndpointResponse> _cachedResponse = new();
readonly static Dictionary<ApiEndpoint, ApiEndpointStatistics> _endpointStatistics = new();

// After:
readonly static ConcurrentDictionary<OnlineSongDetail, CachedApiEndpointResponse> _cachedResponse = new();
readonly static ConcurrentDictionary<ApiEndpoint, ApiEndpointStatistics> _endpointStatistics = new();
```

For each access site identified in Step 2, translate:
| Original | ConcurrentDictionary equivalent |
|----------|---------------------------------|
| `dict[k] = v` | `dict[k] = v` (same; atomic per key) |
| `dict.Add(k, v)` | `dict.TryAdd(k, v)` (do not throw on duplicate; document if behavior change matters) |
| `dict.Remove(k)` | `dict.TryRemove(k, out _)` |
| `dict.ContainsKey(k)` | `dict.ContainsKey(k)` (same) |
| `dict.TryGetValue(k, out v)` | same |
| `dict.Keys`/`Values` | same (snapshot semantics differ — note in commit if iterated) |
| `dict.Clear()` | same |

If any add-then-update pattern exists, use `AddOrUpdate(k, addFactory, updateFactory)`.

Add `using System.Collections.Concurrent;` if not present.

- [ ] **Step 4: Verify**

```bash
rg -n 'SpinLock' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Net/Online.cs
```
Expected: no matches.

```bash
rg -n 'ConcurrentDictionary' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Net/Online.cs
```
Expected: 2 matches (the two fields).

```bash
rg -n 'using System\.Collections\.Concurrent' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Net/Online.cs
```
Expected: one match.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
net: use ConcurrentDictionary in Online cache (H8)

Symptom: Online.cs declared two SpinLock fields that were never used;
  meanwhile two Dictionary<,> fields were accessed from async / threadpool
  paths without any synchronization. .NET dictionary corruption under
  concurrent write is a known heisenbug that surfaces as infinite loops
  in FindEntry.
Why: silent race condition that becomes a CPU-pegged hang in production.
Fix: replace Dictionary<,> with ConcurrentDictionary<,>; translate Add ->
  TryAdd, Remove -> TryRemove, etc. Delete the dead SpinLock declarations.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (H8)
EOF
)"
```

---

## Task 13: C4 — Atomic settings/runtime writes

**Severity:** Critical
**Files:**
- Modify: `Assets/Scripts/Misc/Base/MajEnv.cs`

- [ ] **Step 1: Confirm the non-atomic write site**

```bash
rg -n 'File\.WriteAllText|SettingsPath|_runtimeConfigPath|OnSave' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Base/MajEnv.cs
```

Expected: an `OnSave` (or equivalent) method with two `File.WriteAllText` calls — one for settings, one for runtime config.

- [ ] **Step 2: Read OnSave and the read/recovery path**

Look for the existing `.bak` recovery path on read (around the lines that handle deserialization failure). Confirm what file pattern is expected (`settings.json.bak`, `settings.json.bak.bak`, etc.).

Also identify how Android is detected in the codebase: `rg -n 'Application\.platform|RuntimePlatform\.Android' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Base/MajEnv.cs`.

- [ ] **Step 3: Add a save lock and atomic write helper**

Edit `MajEnv.cs`:

Add at the top of the class (or in an existing static field block):
```csharp
static readonly SemaphoreSlim _saveLock = new(1, 1);
```

Add a private helper method:
```csharp
static void AtomicWriteAllText(string path, string contents)
{
    var tmp = path + ".tmp";
    var bak = path + ".bak";

    // Write to temp file; flush to disk.
    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
    using (var sw = new StreamWriter(fs))
    {
        sw.Write(contents);
        sw.Flush();
        fs.Flush(flushToDisk: true);
    }

    // Replace path atomically; back up the old file to .bak if it exists.
#if UNITY_ANDROID && !UNITY_EDITOR
    // File.Replace is unreliable on some Android filesystems; use a manual rename dance.
    if (File.Exists(path))
    {
        if (File.Exists(bak)) File.Delete(bak);
        File.Move(path, bak);
    }
    File.Move(tmp, path);
#else
    if (File.Exists(path))
    {
        File.Replace(tmp, path, bak);
    }
    else
    {
        File.Move(tmp, path);
    }
#endif
}
```

Then edit `OnSave`:
```csharp
// Before:
var json = Serializer.Json.Serialize(Settings, UserJsonReaderOption);
var json2 = Serializer.Json.Serialize(RuntimeConfig, UserJsonReaderOption);
File.WriteAllText(SettingsPath, json);
File.WriteAllText(_runtimeConfigPath, json2);

// After:
var json = Serializer.Json.Serialize(Settings, UserJsonReaderOption);
var json2 = Serializer.Json.Serialize(RuntimeConfig, UserJsonReaderOption);

_saveLock.Wait();
try
{
    try { AtomicWriteAllText(SettingsPath, json); }
    catch (Exception ex) { MajDebug.LogError($"Failed to write settings: {ex}"); }

    try { AtomicWriteAllText(_runtimeConfigPath, json2); }
    catch (Exception ex) { MajDebug.LogError($"Failed to write runtime config: {ex}"); }
}
finally
{
    _saveLock.Release();
}
```

Also add (in the read path where `.bak` recovery already lives): a check that treats a zero-length file the same as a corrupt file, falling back to `.bak` instead of silently using defaults. Locate the existing fallback first via `rg -n '\.bak' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Base/MajEnv.cs` and adapt that block to also catch `new FileInfo(path).Length == 0`. If the existing code does not have such a block, add a minimal one:

```csharp
// Pseudocode — adapt to actual read path:
if (File.Exists(SettingsPath) && new FileInfo(SettingsPath).Length == 0)
{
    MajDebug.LogWarning($"Settings file at {SettingsPath} is empty; restoring from .bak if available.");
    var bak = SettingsPath + ".bak";
    if (File.Exists(bak)) File.Copy(bak, SettingsPath, overwrite: true);
}
```

- [ ] **Step 4: Verify**

```bash
rg -n 'File\.WriteAllText' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Base/MajEnv.cs
```
Expected: zero matches (or only in non-OnSave paths).

```bash
rg -n 'AtomicWriteAllText|_saveLock' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Base/MajEnv.cs
```
Expected: definition + usage sites present.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
infra/settings: atomic writes for settings and runtime config (C4)

Symptom: MajEnv.OnSave used File.WriteAllText, which truncates then
  writes. Process crash, OS kill, mobile freeze, or power loss between
  truncate and final flush left settings.json zero-byte. The existing
  .bak recovery only handled parse failures, not empty files, so users
  silently fell back to default settings on next launch.
Why: losing every configured setting on a single crash is a hostile UX.
Fix: write to .tmp + flush(disk) + File.Replace(.tmp, path, .bak) on
  desktop, or delete-bak/move-to-bak/move-from-tmp on Android (where
  File.Replace can be unreliable). All wrapped in a SemaphoreSlim to
  serialize concurrent OnSave calls. Read path also treats empty file as
  corrupt and restores from .bak.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (C4)
EOF
)"
```

---

## Task 14: C5 — Expand link.xml to preserve serialized DTOs

**Severity:** Critical
**Files:**
- Modify: `Assets/link.xml`

- [ ] **Step 1: Read current link.xml**

```bash
cat /c/Users/kogeki/dev/MajdataPlay/Assets/link.xml
```

Note current preserve scope (likely just `MajdataPlay.Settings*`).

- [ ] **Step 2: Enumerate every type touched by Newtonsoft.Json reflection**

Run:
```bash
rg -n 'JsonConvert\.|JsonSerializer\.|\[JsonConverter|\[JsonProperty|\[JsonIgnore' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/ -g '*.cs' | head -200
rg -n 'namespace MajdataPlay\.' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/ -g '*.cs' -o | sort -u | head -100
```

Specifically look for:
- All namespaces that contain types serialized to/from JSON.
- All `[JsonConverter(typeof(X))]` attributes (X must be preserved too).
- All DTO classes loaded from `.json` files.

Likely targets (verify each via the rg above):
- `MajdataPlay.Settings.*` (already present)
- `MajdataPlay.Settings.Runtime.*`
- `MajdataPlay.Scenes.Game.JudgeInfo` and `JudgeDetail`
- `MajdataPlay.Misc.Json.JsonConverters.*` (the converter types themselves)
- `MajdataPlay.Net.*` or `MajdataPlay.Misc.Net.*` (Online.cs DTOs)
- `MajdataPlay.Collections.*` (score record types)
- Any `MachineInfo`, `ApiEndpoint`, `GameSetting`, `RuntimeConfig`

- [ ] **Step 3: Rewrite link.xml to preserve all identified namespaces**

Edit `Assets/link.xml`:

```xml
<linker>
  <assembly fullname="Assembly-CSharp">
    <!-- Existing settings preservation -->
    <type fullname="MajdataPlay.Settings*" preserve="all"/>

    <!-- Newly added preservation for JSON-serialized DTOs and their converters -->
    <type fullname="MajdataPlay.Misc.Json.JsonConverters*" preserve="all"/>
    <type fullname="MajdataPlay.Misc.Net*" preserve="all"/>
    <type fullname="MajdataPlay.Net*" preserve="all"/>
    <type fullname="MajdataPlay.Scenes.Game.JudgeInfo" preserve="all"/>
    <type fullname="MajdataPlay.Scenes.Game.JudgeDetail" preserve="all"/>
    <type fullname="MajdataPlay.Collections*" preserve="all"/>
    <!-- Add more here based on Step 2 findings -->
  </assembly>

  <!-- Preserve Newtonsoft.Json reflection targets needed for IL2CPP -->
  <assembly fullname="Newtonsoft.Json" preserve="all"/>
</linker>
```

Replace the placeholder list with the actual exhaustive set you derived in Step 2. The wildcard `*` after a namespace prefix preserves all types in and below that namespace.

If multiple `<assembly>` entries are needed (e.g. plugin assemblies that own DTOs), add them.

CAUTION: do not preserve `Assembly-CSharp` wholesale — that defeats stripping entirely. Be precise.

- [ ] **Step 4: Verify all serialized type namespaces are covered**

```bash
# Re-derive the namespace list from JSON usage sites:
rg -n 'JsonConvert\.Deserialize|JsonConvert\.Serialize|JsonSerializer\.Deserialize|JsonSerializer\.Serialize' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/ -g '*.cs'
```

For each match, read the file to identify the type argument T and its namespace, and confirm that namespace has a corresponding `<type fullname="..." preserve="all"/>` entry in link.xml.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
build/il2cpp: expand link.xml to preserve serialized DTOs (C5)

Symptom: link.xml only preserved MajdataPlay.Settings*. Newtonsoft.Json
  reflects over JudgeInfo, JudgeDetail, score record classes, Net DTOs,
  RuntimeConfig, MachineInfo, ApiEndpoint, etc. Under IL2CPP managed-code
  stripping Medium/High, these were deleted from the build, causing first
  deserialization call on mobile to throw — and the editor never saw it.
Why: shipping a build that crashes on first score-DB load is the worst
  kind of bug: invisible until QA on the target platform.
Fix: enumerate every namespace touched by JsonConvert/JsonSerializer and
  add explicit preserve entries; also preserve Newtonsoft.Json wholesale.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (C5)
EOF
)"
```

---

## Task 15: H10 — Make OBSRecorder cancellable and thread-safe

**Severity:** High
**Files:**
- Modify: `Assets/Scripts/Misc/Recording/OBSRecorder.cs`

- [ ] **Step 1: Confirm the four bugs**

```bash
rg -n 'StartRecordAsync|Authenticate|_webSocket|_obsOutPath|IsConnected|IsRecording|File\.Move' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Recording/OBSRecorder.cs
```

Expected:
- `StartRecordAsync` with `while (!IsConnected) { Authenticate(); await Task.Delay(1000); }` — no token, no max retries.
- `_webSocket = null` somewhere in `Dispose(bool)` with `OnMessageReceived` reading `_webSocket` on a callback thread.
- `IsConnected` / `IsRecording` declared as plain `bool` (no `volatile`).
- `File.Move(_obsOutPath, ...)` where `_obsOutPath` is set in a case branch with no guarantee of ordering vs the move.

- [ ] **Step 2: Read OBSRecorder.cs fully**

It's ~150–300 lines. Read all of it to understand the WebSocket message lifecycle, the OBS case codes, and how the recording stop-then-move flow is sequenced.

- [ ] **Step 3: Apply four targeted edits**

Edit `OBSRecorder.cs`:

**Edit A — make StartRecordAsync cancellable + bounded retry:**
```csharp
// Before:
public async Task StartRecordAsync()
{
    try { while (!IsConnected) { Authenticate(); await Task.Delay(1000); } }
    catch { throw new OBSRecorderException(); }
    await Task.Run(StartRecord);
}

// After:
public async Task StartRecordAsync(CancellationToken cancellationToken = default, int maxAttempts = 30)
{
    try
    {
        var attempts = 0;
        while (!IsConnected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++attempts > maxAttempts)
                throw new OBSRecorderException($"OBS did not become ready after {maxAttempts} attempts.");
            Authenticate();
            await Task.Delay(1000, cancellationToken);
        }
    }
    catch (OperationCanceledException) { throw; }
    catch (OBSRecorderException) { throw; }
    catch (Exception ex) { throw new OBSRecorderException($"OBS connection failed: {ex.Message}", ex); }

    await Task.Run(StartRecord, cancellationToken);
}
```

If `OBSRecorderException` does not take a `(string, Exception)` overload, add one or use the closest available constructor.

**Edit B — mark cross-thread state volatile:**
```csharp
// Before:
public bool IsConnected { get; private set; }
public bool IsRecording { get; private set; }

// After (use backing fields):
volatile bool _isConnected;
volatile bool _isRecording;
public bool IsConnected
{
    get => _isConnected;
    private set => _isConnected = value;
}
public bool IsRecording
{
    get => _isRecording;
    private set => _isRecording = value;
}
```

(If the auto-properties are read in tight loops, this matters. Otherwise it's defense in depth.)

**Edit C — guard _webSocket null race:**
```csharp
// In Dispose(bool):
// Before:
_webSocket = null;
// (immediately, while callbacks may still fire)

// After:
var ws = _webSocket;
_webSocket = null;
ws?.Dispose();   // do disposal AFTER nulling, callbacks now see null
```

In `OnMessageReceived` (or any other callback method), add a null check:
```csharp
var ws = _webSocket;
if (ws is null) return;
// ... use ws (the local) for the rest of the method ...
```

**Edit D — guard File.Move against missing _obsOutPath:**
```csharp
// Find the File.Move call and wrap:
if (string.IsNullOrEmpty(_obsOutPath))
{
    MajDebug.LogWarning("OBSRecorder: _obsOutPath was never set; skipping File.Move.");
}
else
{
    try
    {
        File.Move(_obsOutPath, /* destination */);
    }
    catch (FileNotFoundException ex)
    {
        MajDebug.LogWarning($"OBSRecorder: OBS output file not found at {_obsOutPath}: {ex.Message}");
    }
    catch (IOException ex)
    {
        MajDebug.LogError($"OBSRecorder: File.Move failed: {ex}");
    }
}
```

- [ ] **Step 4: Verify**

```bash
rg -n 'CancellationToken' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Recording/OBSRecorder.cs
```
Expected: present in StartRecordAsync signature.

```bash
rg -n 'volatile bool' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Recording/OBSRecorder.cs
```
Expected: 2 matches.

```bash
rg -B 2 -A 3 'File\.Move' /c/Users/kogeki/dev/MajdataPlay/Assets/Scripts/Misc/Recording/OBSRecorder.cs
```
Expected: try/catch surrounding it.

- [ ] **Step 5: Commit**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay add -A
git -C /c/Users/kogeki/dev/MajdataPlay commit -m "$(cat <<'EOF'
recording: harden OBSRecorder lifecycle (H10)

Symptom: four issues in OBSRecorder:
  (a) StartRecordAsync looped forever waiting for IsConnected with no
      cancellation token and no max retries.
  (b) Dispose set _webSocket = null while OnMessageReceived could be
      concurrently dereferencing it on the callback thread.
  (c) IsConnected/IsRecording were plain bool, read on game thread,
      written on websocket callback thread.
  (d) File.Move(_obsOutPath, ...) was called without verifying the path
      had been set by the corresponding event branch first.
Why: any of (a)-(d) can hang the recorder or crash the app under normal OBS
  reconnect / stop-mid-record scenarios.
Fix: (a) add CancellationToken + bounded retries; (b) cache _webSocket to
  local before disposing, null check in callbacks; (c) volatile backing
  fields for the bools; (d) null-check _obsOutPath, try/catch File.Move.

Refs: docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md (H10)
EOF
)"
```

---

## Final Tasks: push and codex review

### Task 16: Push branch to fork

- [ ] **Step 1: Sanity check the branch state**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay log --oneline upstream/dev..fix/critical-and-high
git -C /c/Users/kogeki/dev/MajdataPlay status
```
Expected: 16 commits (1 design doc + 15 fixes); clean working tree.

- [ ] **Step 2: Push**

```bash
git -C /c/Users/kogeki/dev/MajdataPlay push -u origin fix/critical-and-high
```

Expected: branch created on `kogekiplay/MajdataPlay`.

### Task 17: Codex review

- [ ] **Dispatch a fresh codex agent on a tight, well-scoped review**

Use `mcp__codeg-delegate__delegate_to_agent` with `agent_type: codex`, `working_dir: C:\Users\kogeki\dev\MajdataPlay`. Prompt:

> rg is installed; use it. Branch under review: `fix/critical-and-high`. Compare against `upstream/dev`. The diff implements 15 specific bug fixes documented in `docs/superpowers/specs/2026-05-27-critical-and-high-fixes-design.md`. For each commit, verify: (1) the bug it claims to fix is actually gone in the new code, (2) the fix does not introduce a new bug, (3) the fix is minimal (no drive-by refactors). Produce a punch list — for each of the 16 commits, "OK / Concern: <one line>" — and a final verdict (Approve / Request changes / Reject) in under 1000 words. Use `git log upstream/dev..HEAD --oneline` to enumerate commits. Do not modify files.

If codex hangs >10 minutes, cancel and either retry with tighter scope (per-commit) or do the review ourselves.

---

## Self-Review Notes

- **Spec coverage**: 15 fixes ↔ 15 tasks ↔ commits 1–15. Push and codex review = tasks 16–17. ✓
- **Placeholder scan**: all code blocks contain concrete content; no "implement later" or "similar to Task N". TDD adaptation is documented upfront (no automated test infra exists). ✓
- **Type consistency**: `_saveLock` / `AtomicWriteAllText` / `_dataHandle` / `_isConnected` are used the same way in their declaration and usage steps. ✓
- **Out of scope honoured**: no Medium/Low fixes, no CI changes, no test infrastructure. ✓
- **Per-task self-containment**: each task includes all `rg` commands and context excerpts a fresh subagent needs to execute without seeing prior tasks. ✓

---

*End of plan.*
