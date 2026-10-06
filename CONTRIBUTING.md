# Contributing to PhoneGrade

Thank you for considering contributing. This document covers what a contribution is,
what happens to the copyright in it, and what is expected of code that goes in.

The project is published so that a shop owner can read it and check it rather than take
a promise on trust. Contributions are welcome on the same basis: they are read, they
are tested, and the person who wrote them keeps their name on them.

## What we are looking for

- Bugs reproduced with a device, a serial and a description of what you saw.
- Fixes with a test that fails before the fix and passes after it.
- Translations, corrections and rewording. These are as welcome as code.
- Accessibility fixes.
- Documentation that says how to do something that the documentation does not cover.

## What we are not looking for

- Refactoring with no observable benefit.
- New dependencies, particularly large ones.
- Features that make the tool a general-purpose device management system. PhoneGrade
  grades phones. Features that do not serve that are out of scope, however good they
  are.
- Code that phones home, collects anything about the device or the operator, or
  requires an account to work.

## How to contribute

1. Open an issue first for anything larger than a one-line fix, so we can agree on the
   approach before you spend a weekend on it.
2. Work on a branch.
3. Make the tests pass. A contribution without a test, where a test is possible, will
   not be merged.
4. Match the surrounding code style rather than introducing a new one.
5. Keep comments for the reasoning, not for restating the code. If a line needs a
   comment to explain what it does, the line should be clearer.
6. Open a pull request with a description in plain language of what changes and why.

## Contributor Licence Agreement

Contributions are accepted under the terms below. In short: you keep your copyright in
your own work, and you let the project use it under the project's licence.

### Copyright notice

Copyright in each contribution remains with the contributor who wrote it. The author of
the project, WarreTh, sole trader in Belgium, holds the copyright in the project as a
whole and in the original code, and continues to be identified as the original author
and copyright holder.

### The agreement

By opening a pull request, you agree to the following.

1. **You have the right to submit this contribution.** You are its author, or you have
   written permission from everyone who has a right in it. You are not submitting work
   copied from a project with a different licence, and you are not submitting anything
   you received under a confidentiality obligation.

2. **You grant a copyright licence to the project.** You grant the project a perpetual,
   worldwide, non-exclusive, royalty-free, irrevocable copyright licence to use,
   reproduce, modify, adapt, publish and distribute your contribution as part of the
   project, under whatever licence the project is published under at the time, and to
   sublicense it on those terms.

3. **You grant a patent licence.** You grant a perpetual, worldwide, non-exclusive,
   royalty-free, irrevocable patent licence covering the claims your contribution
   infringes, to make, have made, use, offer for sale, sell and import the project.
   This licence ends for you if you make a written patent claim against the project or
   a company in your name makes one on your behalf.

4. **You confirm the contribution is your original work.** If your employer has rights
   to work you produce, you have obtained permission to contribute it, or your employer
   has waived those rights.

5. **No warranty.** The contribution is provided as is. You make no warranty about it,
   and you accept that the project is provided without warranty of any kind.

6. **Attribution stays.** The project's existing author, copyright notices, licence
   notices and credits must not be removed. If your contribution is added, it is added
   alongside them, not instead of them.

7. **No separate commercial licence is created.** Contributing does not make you a
   licensee of the project, does not entitle you to any commercial use of the project,
   and does not give you any right to use the PhoneGrade name or logo. Commercial use
   by a business is governed by the commercial end user licence agreement, and the marks
   are governed by the trademark policy.

8. **No obligation to publish.** The project may decline or withdraw a contribution. If
   it is withdrawn before release, the licences in points 2 and 3 end for it.

### How we record your copyright

Each pull request records that you have accepted this agreement, and a CLA bot asks for
your explicit acceptance. If your employer requires a signed agreement rather than an
email confirmation, ask before you contribute and the paperwork will be arranged.

By contributing you agree that your name and the fact of your contribution may be shown
in the project's credits, in its changelog, and in the commit history, and that your
contribution will remain attributed in those places after a relicensing.

## The licence the code is under

The source code is published under the **PolyForm Noncommercial 1.0.0** licence. Read
the `LICENSE` file before you contribute. It permits use, change and distribution for
noncommercial purposes, requires that the licence and the required notice travel with
any copy, and does not permit commercial use.

Two consequences worth knowing before you send anything.

Contribute only code you are able to license under whatever licence the project is
published under at the time. That means checking what that is before you start rather
than after, because a contribution under terms the project cannot carry is a
contribution that cannot be merged and the work does not come back.

If you expect your contribution to be used commercially, raise it in an issue before
you write it. Some parts may need different terms, and that is easier to agree before
anyone has spent a weekend on it.

## Reporting a security issue

Do not open a public issue for a security problem. Email hello@phonegrade.app with
enough detail to reproduce it. You will get an acknowledgement, and you will be told
what is fixed and when.

## Code of conduct

Be decent to each other. Assume the other person is trying to help. Review the code, not
the person. "That will not work because the device returns NOCOLOR on that model" is
useful; "that is wrong" is not. Disagreement about a design decision is fine and is
resolved by arguing about the problem, not about each other.

Harassment, personal attacks, and unwelcome sexual attention are not tolerated and will
be dealt with by ending the interaction.

## Licence and trademark documents

- `LICENSE`: PolyForm Noncommercial 1.0.0, the licence the code is published under.
- `COMMERCIAL_EULA.md`: what a paying business customer may and may not do.
- `TRADEMARK_POLICY.md`: what you may and may not do with the name and the logo.
- `CONTRIBUTING.md`: this document, including the contributor licence agreement.

---

Questions about any of this: hello@phonegrade.app.