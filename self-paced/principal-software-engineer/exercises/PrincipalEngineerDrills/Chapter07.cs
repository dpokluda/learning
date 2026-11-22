namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter07
{
    [TestMethod]
    public void Covering_index_eliminates_clustered_bookmark_lookups()
    {
        IndexedTable table = new IndexedTable();
        table.Insert(new DeviceRow("d-1", "tenant-a", "Healthy", "4.2.1"));
        table.Insert(new DeviceRow("d-2", "tenant-a", "Healthy", "4.2.0"));
        table.Insert(new DeviceRow("d-3", "tenant-b", "Unhealthy", "4.1.0"));

        (IReadOnlyList<string> versionsViaNonCovering, int bookmarkLookups1) =
            table.QueryByTenantAndStatus("tenant-a", "Healthy", useCoveringIndex: false);
        (IReadOnlyList<string> versionsViaCovering, int bookmarkLookups2) =
            table.QueryByTenantAndStatus("tenant-a", "Healthy", useCoveringIndex: true);

        Assert.HasCount(2, versionsViaNonCovering);
        Assert.AreEqual(2, bookmarkLookups1);
        Assert.HasCount(2, versionsViaCovering);
        Assert.AreEqual(0, bookmarkLookups2);
    }

    [TestMethod]
    public void Identity_map_and_unit_of_work_return_same_reference_and_commit_atomically()
    {
        Dictionary<string, DeviceRow> database = new Dictionary<string, DeviceRow>(StringComparer.Ordinal)
        {
            ["d-1"] = new DeviceRow("d-1", "tenant-a", "Healthy", "4.1.0")
        };

        UnitOfWorkSession uow = new UnitOfWorkSession(database);
        MutableDevice? firstLoad = uow.Load("d-1");
        MutableDevice? secondLoad = uow.Load("d-1");

        Assert.IsNotNull(firstLoad);
        Assert.AreSame(firstLoad, secondLoad);

        firstLoad.FirmwareVersion = "4.2.1";
        uow.MarkDirty(firstLoad);
        Assert.AreEqual("4.1.0", database["d-1"].FirmwareVersion);

        uow.Commit();
        Assert.AreEqual("4.2.1", database["d-1"].FirmwareVersion);
    }
}

file sealed record DeviceRow(string DeviceId, string TenantId, string Status, string FirmwareVersion);

file sealed class IndexedTable
{
    private readonly SortedDictionary<string, DeviceRow> _clusteredIndex = new SortedDictionary<string, DeviceRow>(StringComparer.Ordinal);
    private readonly Dictionary<(string TenantId, string Status), List<string>> _nonCoveringIndex =
        new Dictionary<(string, string), List<string>>();
    private readonly Dictionary<(string TenantId, string Status), List<(string DeviceId, string FirmwareVersion)>> _coveringIndex =
        new Dictionary<(string, string), List<(string, string)>>();

    public void Insert(DeviceRow row)
    {
        _clusteredIndex[row.DeviceId] = row;
        (string, string) compositeKey = (row.TenantId, row.Status);

        if (!_nonCoveringIndex.TryGetValue(compositeKey, out List<string>? pkList))
        {
            pkList = new List<string>();
            _nonCoveringIndex[compositeKey] = pkList;
        }

        pkList.Add(row.DeviceId);

        if (!_coveringIndex.TryGetValue(compositeKey, out List<(string, string)>? coveredList))
        {
            coveredList = new List<(string, string)>();
            _coveringIndex[compositeKey] = coveredList;
        }

        coveredList.Add((row.DeviceId, row.FirmwareVersion));
    }

    public (IReadOnlyList<string> Versions, int ClusteredBookmarkLookups) QueryByTenantAndStatus(
        string tenantId,
        string status,
        bool useCoveringIndex)
    {
        List<string> results = new List<string>();
        int bookmarkLookups = 0;
        (string, string) key = (tenantId, status);

        if (useCoveringIndex)
        {
            if (_coveringIndex.TryGetValue(key, out List<(string DeviceId, string FirmwareVersion)>? coveredRows))
            {
                foreach ((string _, string version) in coveredRows)
                {
                    results.Add(version);
                }
            }

            return (results, 0);
        }

        if (_nonCoveringIndex.TryGetValue(key, out List<string>? pkList))
        {
            foreach (string pk in pkList)
            {
                bookmarkLookups++;
                results.Add(_clusteredIndex[pk].FirmwareVersion);
            }
        }

        return (results, bookmarkLookups);
    }
}

file sealed class MutableDevice
{
    public string DeviceId { get; }

    public string TenantId { get; }

    public string Status { get; set; }

    public string FirmwareVersion { get; set; }

    public MutableDevice(string deviceId, string tenantId, string status, string firmwareVersion)
    {
        DeviceId = deviceId;
        TenantId = tenantId;
        Status = status;
        FirmwareVersion = firmwareVersion;
    }
}

file sealed class UnitOfWorkSession
{
    private readonly Dictionary<string, DeviceRow> _database;
    private readonly Dictionary<string, MutableDevice> _identityMap = new Dictionary<string, MutableDevice>(StringComparer.Ordinal);
    private readonly HashSet<MutableDevice> _dirtyEntities = new HashSet<MutableDevice>();

    public UnitOfWorkSession(Dictionary<string, DeviceRow> database)
    {
        _database = database;
    }

    public MutableDevice? Load(string deviceId)
    {
        if (_identityMap.TryGetValue(deviceId, out MutableDevice? cached))
        {
            return cached;
        }

        if (!_database.TryGetValue(deviceId, out DeviceRow? row))
        {
            return null;
        }

        MutableDevice entity = new MutableDevice(row.DeviceId, row.TenantId, row.Status, row.FirmwareVersion);
        _identityMap[deviceId] = entity;
        return entity;
    }

    public void MarkDirty(MutableDevice entity)
    {
        _dirtyEntities.Add(entity);
    }

    public void Commit()
    {
        foreach (MutableDevice dirty in _dirtyEntities)
        {
            _database[dirty.DeviceId] = new DeviceRow(dirty.DeviceId, dirty.TenantId, dirty.Status, dirty.FirmwareVersion);
        }

        _dirtyEntities.Clear();
    }
}
