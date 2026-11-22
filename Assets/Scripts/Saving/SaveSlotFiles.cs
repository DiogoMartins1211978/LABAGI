using System;
using System.IO;
using System.Text;

// File operations are independent of Unity so failure/recovery can be tested.
public sealed class SaveSlotFiles
{
    readonly string directory;
    readonly Action<string, int> validate;
    const int MaxBytes = 4 * 1024 * 1024;

    public SaveSlotFiles(string directory, Action<string, int> validate)
    {
        this.directory = directory;
        this.validate = validate;
    }

    public string PathFor(int slot)
    {
        if (slot < 1 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot));
        return Path.Combine(directory, "slot-" + slot + ".json");
    }

    public bool Exists(int slot) => File.Exists(PathFor(slot)) || File.Exists(PathFor(slot) + ".bak");

    string ReadFile(string path, int slot)
    {
        if (new FileInfo(path).Length > MaxBytes) throw new IOException("Save file is too large.");
        string json = File.ReadAllText(path, Encoding.UTF8);
        validate(json, slot);
        return json;
    }

    public string Read(int slot, out bool recovered)
    {
        recovered = false;
        string path = PathFor(slot);
        try { return ReadFile(path, slot); }
        catch (Exception primary) when (primary is IOException || primary is UnauthorizedAccessException
            || primary is FormatException || primary is ArgumentException)
        {
            if (!File.Exists(path + ".bak")) throw;
            string json = ReadFile(path + ".bak", slot);
            recovered = true;
            return json;
        }
    }

    public void Write(int slot, string json, bool allowOverwrite)
    {
        string path = PathFor(slot);
        validate(json, slot);
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new IOException("Save file is too large.");
        if (!allowOverwrite && Exists(slot)) throw new IOException("That slot already contains a save.");
        Directory.CreateDirectory(directory);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
        ReadFile(temp, slot);

        // Keep only validated old data as the backup; never replace a healthy
        // backup with a corrupt primary after recovering a save.
        bool primaryValid = false;
        if (File.Exists(path))
        {
            try { ReadFile(path, slot); primaryValid = true; }
            catch (Exception e) when (e is IOException || e is FormatException || e is ArgumentException) { }
        }
        if (primaryValid) File.Copy(path, path + ".bak", true);
        if (File.Exists(path))
        {
            try { File.Replace(temp, path, null); }
            catch (PlatformNotSupportedException) { ReplaceWithRecovery(temp, path); }
        }
        else File.Move(temp, path);
        // A first save also gets a recovery copy.
        if (!File.Exists(path + ".bak")) File.Copy(path, path + ".bak");
    }

    static void ReplaceWithRecovery(string temp, string path)
    {
        // The validated backup remains available if the process stops here.
        File.Delete(path);
        File.Move(temp, path);
    }
}
