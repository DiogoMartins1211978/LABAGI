// Standalone .NET console check; compile alongside SaveSlotFiles.cs.
using System;
using System.IO;

static class SaveSlotFilesChecks
{
    static int checks;
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
    }

    static void Reject(Action action, string description)
    {
        bool rejected = false;
        try { action(); }
        catch (IOException) { rejected = true; }
        catch (FormatException) { rejected = true; }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, description);
    }

    public static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "labagi-save-check-" + Guid.NewGuid().ToString("N"));
        var files = new SaveSlotFiles(root, (json, slot) =>
        {
            if (!json.StartsWith("valid:" + slot + ":", StringComparison.Ordinal))
                throw new FormatException("Invalid payload or wrong slot.");
        });
        for (int slot = 1; slot <= 3; slot++)
        {
            files.Write(slot, "valid:" + slot + ":first", false);
            Check(files.Read(slot, out bool recovered) == "valid:" + slot + ":first" && !recovered,
                "Each slot must round-trip independently.");
        }
        Reject(() => files.PathFor(0), "Slot zero must be rejected.");
        Reject(() => files.PathFor(4), "Slot four must be rejected.");
        Reject(() => files.Write(1, "valid:1:replacement", false), "New game must protect occupied slots.");
        Reject(() => files.Write(1, "valid:2:wrong-slot", true), "A mismatched slot must be rejected.");
        Reject(() => files.Write(1, "corrupt", true), "Invalid new data must be rejected before touching the save.");
        Check(files.Read(1, out _) == "valid:1:first", "Rejected writes must preserve the primary.");

        files.Write(1, "valid:1:second", true);
        File.WriteAllText(files.PathFor(1), "corrupt");
        Check(files.Read(1, out bool backup) == "valid:1:first" && backup, "Corrupt primary must recover its backup.");
        files.Write(1, "valid:1:third", true);
        Check(File.ReadAllText(files.PathFor(1) + ".bak") == "valid:1:first", "Recovery must not overwrite the healthy backup with corruption.");
        Check(files.Read(2, out _) == "valid:2:first", "Other slots must remain unchanged.");

        File.Delete(files.PathFor(3));
        Check(files.Read(3, out backup) == "valid:3:first" && backup, "Missing primary must recover its backup.");
        Reject(() => files.Write(3, "valid:3:new", false), "A backup-only slot is still occupied.");
        File.WriteAllText(files.PathFor(2) + ".tmp", "interrupted write");
        Check(files.Read(2, out _) == "valid:2:first", "Interrupted temporary files must not replace a save.");
        using (var held = new FileStream(files.PathFor(2) + ".tmp", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Reject(() => files.Write(2, "valid:2:blocked", true), "Write failures must be reported.");
        Check(files.Read(2, out _) == "valid:2:first", "A failed write must preserve the previous save.");
        File.WriteAllText(files.PathFor(3) + ".bak", "corrupt too");
        Reject(() => files.Read(3, out _), "An unusable slot must fail rather than return empty progress.");
        Console.WriteLine("PASS: " + checks + " save-file checks. Temporary test folder: " + root);
    }
}
