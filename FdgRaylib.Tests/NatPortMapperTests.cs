using FdgRaylib.ListServer;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #264: NatPortMapper's discovery/mapping/timeout path talks to a real router (SSDP + NAT-PMP), so
// calling Start() from an automated test would send live network traffic and could actually open a
// port on the dev machine's router. That path is verified by hand against a real router (see
// TESTING-CHECKLIST.md). These tests cover only the network-free invariants: the initial surface and
// the dispose-before-mapped teardown that the real lobby/game/app-exit paths exercise.
[TestFixture]
public class NatPortMapperTests
{
    [Test]
    public void NewMapper_IsIdle_WithEmptyStatus()
    {
        using var mapper = new NatPortMapper(6389);
        Assert.That(mapper.State, Is.EqualTo(NatPortMapper.MapState.Idle));
        Assert.That(mapper.Status, Is.Empty);
    }

    [Test]
    public void Dispose_BeforeStart_IsSafeAndIdempotent()
    {
        var mapper = new NatPortMapper(6389);
        Assert.DoesNotThrow(() => mapper.Dispose());
        Assert.DoesNotThrow(() => mapper.Dispose());
        Assert.That(mapper.State, Is.EqualTo(NatPortMapper.MapState.Idle));
    }
}
