## What and why

Issue: #

## Kind of change

- [ ] Extend: new code beside shipped code
- [ ] Replace and repoint: a fix to a shipped rule, with the old class tagged `[Obsolete]`
- [ ] Change: the issue says CHANGE, and names the types
- [ ] Docs, tests or build only

## Checklist

- [ ] The branch is from `dev`, and this PR targets `dev`.
- [ ] `dotnet build RecDiffy.NET/RecDiffy.slnx -c Release -warnaserror --no-incremental` is clean.
- [ ] `dotnet test RecDiffy.NET/RecDiffy.slnx -c Release` passes.
- [ ] If the Excel add-in changed: its solution builds with `-warnaserror`, and its tests pass.
- [ ] New tests are in new files.
- [ ] All test data is synthetic.

## 🔴 Bugs found

List each bug you found along the way, with its issue and its skipped failing test. Write "None" if there are none.
