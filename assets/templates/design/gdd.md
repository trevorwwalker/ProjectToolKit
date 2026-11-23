# {{PROJECT}} - Design

Status: draft v0.1 · Owner: <who> · See also [conventions](../conventions.md) and
[the technical design](../tdd/{{PROJECT}}-technical.md)

What the thing is and what it must do: intent, rules, values and feel. The human and the frontier
model write this; workers read it and do not decide it. It wins over the technical document on what
the product should do, and loses on technical parameters. **Fill in** every section; delete the
guidance lines as you go.

## 1. High concept

One paragraph a stranger can repeat back: the premise, the player or user, and the inversion or idea
that makes it worth building. Finish with an elevator pitch of two sentences.

## 2. Design pillars

Three to five, each one a test a decision can fail. For each: the sentence, and what it rules out.

## 3. Core loop

The loop someone runs repeatedly, moment to moment, and why they run it again. A diagram is worth
one paragraph here.

## 4. The experience

One section per surface or area, in the order someone meets it. Be exact about the numbers a worker
cannot infer (sizes, timings, counts, thresholds) and say which of them are provisional.

## 5. Rules and values

The mechanics: what is allowed, what costs what, and the numbers that define it. Anything here that
the application loads lives in `assets/data/` and exists nowhere else - this section describes it,
the data file holds it. Mark a number `PROVISIONAL` if it was guessed.

## 6. Structure

How a session is bounded: what starts it, what ends it, what happens between, and the win or
completion condition.

## 7. Fairness and anti-frustration

The rules that stop the thing from being unfair or dead in the hand, and why each is load-bearing.
State what must never happen.

## 8. Feel, art and audio

What it should look and sound like, and what a piece of feedback exists to communicate. If a cue is
information rather than decoration, say so: it is then a rule, not a preference.

## 9. Interaction

Controls and inputs, one per action, with the state each is valid in. Say what happens on a
conflict, a cancel and an idle.

## 10. Technical direction

What this document asks of the architecture, without deciding it: the properties that must hold
(for example deterministic and replayable, headless-capable, moddable), and the constraints the
technical design must respect.

## 11. Open questions

Numbered, each with what is missing and what would settle it. A worker that hits one parks instead
of guessing.

## 12. Milestones

The order the experience becomes visible, each milestone something you can look at and judge, not a
list of modules.
