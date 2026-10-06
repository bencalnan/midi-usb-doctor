using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Tests;

public sealed class DeviceLabelStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MidiUsbDoctorTests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "nested", "device-labels.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_loads_as_empty_store()
    {
        var store = DeviceLabelStore.LoadFrom(FilePath);

        Assert.Empty(store.Ports);
        Assert.Null(store.GetLabel("CME [H12] Port 1"));
    }

    [Fact]
    public void Labels_and_connections_round_trip_through_disk_and_create_the_folder()
    {
        var store = new DeviceLabelStore(FilePath);
        store.Set("CME [H12] Port 1", new DevicePortInfo("  Bass Station II ", " USB host 3 "));
        store.SetLabel("CME [H12] Port 2", "Roland S-1");
        store.SetConnection("CME [H12] Port 3", "DIN 1");
        store.Save();

        var reloaded = DeviceLabelStore.LoadFrom(FilePath);

        Assert.Equal(new DevicePortInfo("Bass Station II", "USB host 3"), reloaded.Get("CME [H12] Port 1"));
        Assert.Equal("Roland S-1", reloaded.GetLabel("cme [h12] port 2"));
        Assert.Null(reloaded.GetConnection("CME [H12] Port 2"));
        Assert.Equal("DIN 1", reloaded.GetConnection("CME [H12] Port 3"));
        Assert.Equal(3, reloaded.Ports.Count);
    }

    [Fact]
    public void Setting_one_field_keeps_the_other()
    {
        var store = new DeviceLabelStore(FilePath);
        store.SetLabel("CME [H12] Port 1", "Bass Station II");

        store.SetConnection("CME [H12] Port 1", "USB host 3");

        Assert.Equal("Bass Station II", store.GetLabel("CME [H12] Port 1"));
        Assert.Equal("USB host 3", store.GetConnection("CME [H12] Port 1"));
    }

    [Fact]
    public void Clearing_both_fields_removes_the_entry()
    {
        var store = new DeviceLabelStore(FilePath);
        store.Set("CME [H12] Port 3", new DevicePortInfo("Roland T-8", "USB host 1"));

        store.Set("CME [H12] Port 3", new DevicePortInfo("   ", null));

        Assert.Null(store.Get("CME [H12] Port 3"));
        Assert.Empty(store.Ports);
    }

    [Fact]
    public void Version_1_label_only_files_still_load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{ "Version": 1, "Labels": { "CME [H12] Port 1": "Bass Station II" } }""");

        var store = DeviceLabelStore.LoadFrom(FilePath);

        Assert.Equal(new DevicePortInfo("Bass Station II", null), store.Get("CME [H12] Port 1"));
    }

    [Fact]
    public void Corrupt_file_loads_as_empty_store_and_can_be_overwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ this is not json");

        var store = DeviceLabelStore.LoadFrom(FilePath);
        Assert.Empty(store.Ports);

        store.SetLabel("CME [H12] Port 1", "Bass Station II");
        store.Save();

        Assert.Equal("Bass Station II", DeviceLabelStore.LoadFrom(FilePath).GetLabel("CME [H12] Port 1"));
    }

    [Fact]
    public void Entries_for_absent_ports_are_kept()
    {
        var store = new DeviceLabelStore(FilePath);
        store.SetLabel("CME [H12] Port 1", "Bass Station II");
        store.SetLabel("Old Interface Port 4", "Volca");
        store.Save();

        var reloaded = DeviceLabelStore.LoadFrom(FilePath);
        reloaded.SetLabel("CME [H12] Port 1", "Bass Station 2");
        reloaded.Save();

        Assert.Equal("Volca", DeviceLabelStore.LoadFrom(FilePath).GetLabel("Old Interface Port 4"));
    }
}
