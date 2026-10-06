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

        Assert.Empty(store.Labels);
        Assert.Null(store.GetLabel("CME [H12] Port 1"));
    }

    [Fact]
    public void Labels_round_trip_through_disk_and_create_the_folder()
    {
        var store = new DeviceLabelStore(FilePath);
        store.SetLabel("CME [H12] Port 1", "  Bass Station II ");
        store.SetLabel("CME [H12] Port 2", "Roland S-1");
        store.Save();

        var reloaded = DeviceLabelStore.LoadFrom(FilePath);

        Assert.Equal("Bass Station II", reloaded.GetLabel("CME [H12] Port 1"));
        Assert.Equal("Roland S-1", reloaded.GetLabel("cme [h12] port 2"));
        Assert.Equal(2, reloaded.Labels.Count);
    }

    [Fact]
    public void Blank_label_removes_the_entry()
    {
        var store = new DeviceLabelStore(FilePath);
        store.SetLabel("CME [H12] Port 3", "Roland T-8");

        store.SetLabel("CME [H12] Port 3", "   ");

        Assert.Null(store.GetLabel("CME [H12] Port 3"));
        Assert.Empty(store.Labels);
    }

    [Fact]
    public void Corrupt_file_loads_as_empty_store_and_can_be_overwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ this is not json");

        var store = DeviceLabelStore.LoadFrom(FilePath);
        Assert.Empty(store.Labels);

        store.SetLabel("CME [H12] Port 1", "Bass Station II");
        store.Save();

        Assert.Equal("Bass Station II", DeviceLabelStore.LoadFrom(FilePath).GetLabel("CME [H12] Port 1"));
    }

    [Fact]
    public void Labels_for_absent_ports_are_kept()
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
