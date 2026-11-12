# RecDiffy Excel add-in privacy policy

*In effect from 12 November 2026.*

This policy covers the RecDiffy Excel add-in and its installer, published by the RecDiffy.NET project at <https://github.com/m-vaysman/recdiffy.net>.

## In short

- **No personal information:** the add-in doesn't collect any.
- **Nothing sent over the network.** It has no telemetry, crash reports, update checks or accounts.
- **Two kinds of file on your computer:** a log and its settings. Only your Windows account can read them.

## What the add-in reads

It reads only the data you choose:
- a range in an open workbook;
- a closed workbook;
- a CSV file.

It reads that data inside Excel, on your computer. When you choose **Write to worksheet**, it puts the results in a new workbook. It never saves that workbook itself. That's up to you.

## What it stores on your computer

### The log

- **Where:** `%LOCALAPPDATA%\RecDiffy\Logs\RecDiffy-<date>-<process id>.log`
- **What's in it:** what the add-in did and when:
  - the names of the files and sheets it read, without their folders;
  - cell ranges;
  - row and column counts;
  - timings;
  - result counts;
  - settings changes;
  - warnings and errors.
- **What's not:** it doesn't record the values in your data rows. An error message can name a sheet, or quote the column names in a header row.
- **How long:** when the add-in loads, it deletes log files older than 14 days.

### The settings

- **Where:** `%APPDATA%\RecDiffy\settings.json`
- **What's in it:** the two switches on the **Settings** tab.
- **How long:** until you delete it.

### What the installer writes

- **The add-in's files,** in `%LOCALAPPDATA%\Programs\RecDiffy`.
- **The install folder's location,** under `HKEY_CURRENT_USER\Software\RecDiffy\ExcelAddIn`.
- **An entry in Excel's list of add-ins to load:** an `OPEN` value under `HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Excel\Options`.

To find Excel and its bitness, the installer reads Excel's location from the registry, and the header of Excel's program file.

Uninstalling removes the add-in's files and both registry entries. It doesn't remove the log or the settings. To remove those, delete the `%LOCALAPPDATA%\RecDiffy` and `%APPDATA%\RecDiffy` folders.

## How it's protected

The files are in your Windows user profile, so the permissions on your Windows account protect them. They aren't encrypted.

If your organization uses roaming profiles, Windows may copy the settings file to the organization's servers. It does the same with everything in `%APPDATA%`.

## Who sees it

Nobody but you. The add-in shares nothing with the RecDiffy.NET project or anyone else.

Information leaves your computer only if you send it yourself. For example:
- **Copy** in the log window puts the log's entries on the clipboard;
- you might attach a log file to a GitHub issue.

Before you share a log, read it. It names the files and sheets you worked with.

## Your choices

- **See what's stored:** the log window shows this Excel session's entries. To open it, go to the **Add-ins** tab, then **RecDiffy → Show log**. The files above are plain text.
- **Delete it:** you can delete those files at any time. The add-in starts a new log, and goes back to the default settings.
- **Stop it:** uninstall the add-in in Settings → Apps. It then writes nothing more.

## Other services

- **GitHub:** if you download the installer from GitHub, [GitHub's privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement) applies to the download.
- **Microsoft Store:** if you install from the Microsoft Store, [Microsoft's privacy statement](https://privacy.microsoft.com/privacystatement) applies to the Store.
- **Excel:** Microsoft's privacy statement also covers Excel itself. The add-in doesn't change Excel's privacy or diagnostic settings.
- **Excel-DNA file logging:** the add-in is built with Excel-DNA. Its file logging is off unless you turn it on with the environment variables in the README's [Troubleshooting](README.md#troubleshooting) section. Then Excel-DNA writes its own log, to the file you name.

## Changes and questions

When this policy changes, this page changes. The repository's history keeps every version.

- **Questions:** open an issue at <https://github.com/m-vaysman/recdiffy.net/issues>.
- **Security problems:** report them privately, as the [security policy](../SECURITY.md) describes.
