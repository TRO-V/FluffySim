#pragma warning disable CA1416
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Dsr;
using AnoMech.Scenarios.Dsr.P5Death;

namespace AnoMech.Tests;

public class DsrP5DeathSouthTests
{
    private static readonly PartyRole[] Priority =
        [PartyRole.MainTank, PartyRole.OffTank, PartyRole.MeleeDpsA, PartyRole.MeleeDpsB,
            PartyRole.PhysRangedDps, PartyRole.CasterDps, PartyRole.RegenHealer, PartyRole.ShieldHealer];

    [Test]
    public void BothStrategiesRemainSelectable()
    {
        var strategies = new DsrP5DeathScenario().AiStrats;
        Assert.That(strategies.Select(s => s.Name), Is.EqualTo(new[]
            { "Dooms North (2-2, anchored)", "Dooms South (2-2, anchored)" }));
    }

    [Test]
    public void SouthCongaPrioritizesDpsBeforeHealers()
    {
        Assert.That(DsrP5DeathAi.CongaLineWithThePlayerAt(null, PartyRole.MainTank, true), Is.EqualTo(Priority));
        foreach (var player in Priority)
        for (var slot = 0; slot < 8; slot++)
        {
            var expected = Priority.Where(r => r != player).ToList();
            expected.Insert(slot, player);
            Assert.That(DsrP5DeathAi.CongaLineWithThePlayerAt(slot, player, true), Is.EqualTo(expected));
        }
    }

    [TestCase(0f, false)] [TestCase(90f, false)] [TestCase(180f, false)] [TestCase(270f, false)]
    [TestCase(0f, true)] [TestCase(90f, true)] [TestCase(180f, true)] [TestCase(270f, true)]
    public void MiddleDoomsSpreadSouthThenAnchorNorthAndCleanse(float bearing, bool firstSquare)
    {
        var scenario = new DsrP5DeathScenario();
        var settings = (DsrP5DeathStateOverrides)scenario.SettingsOverrides;
        settings.WarriorBearing = bearing;
        settings.FirstDoomSquare = firstSquare;
        settings.PlayerDoom = null;
        settings.Dooms = [PartyRole.MainTank, PartyRole.MeleeDpsA, PartyRole.PhysRangedDps, PartyRole.RegenHealer];
        var dooms = Priority.Where(settings.Dooms.Contains).ToArray();
        var cleanses = Priority.Where(r => !settings.Dooms.Contains(r)).ToArray();
        var checkedSpreads = false;
        var checkedAnchors = false;
        var checkedCleanse = false;
        var north = DsrP5Constants.AtBearing(bearing, 1);
        var east = DsrP5Constants.AtBearing(bearing + 90, 1);
        var run = ScenarioRun.Execute(typeof(DsrP5DeathScenario), 1, 47, new()
        {
            ScenarioFactory = () => scenario,
            Probe = p =>
            {
                if (p.Crossed(23.9f))
                {
                    checkedSpreads = true;
                    for (var i = 0; i < 4; i++)
                    {
                        var outer = i is 0 or 3;
                        var side = i < 2 ? -1 : 1;
                        var middleNorth = 19.5f * MathF.Cos(40 * MathF.PI / 180);
                        var middleEast = 19.5f * MathF.Sin(40 * MathF.PI / 180);
                        var doomSpot = north * (outer ? 0 : -middleNorth) + east * side * (outer ? 14 : middleEast);
                        var cleanseSpot = north * (outer ? 0 : middleNorth) + east * side * (outer ? 20 : middleEast);
                        Assert.That(Vector3.Distance(p.Member(dooms[i])!.Position, doomSpot), Is.LessThan(.1f));
                        Assert.That(Vector3.Distance(p.Member(cleanses[i])!.Position, cleanseSpot), Is.LessThan(.1f));
                    }
                }
                if (p.Crossed(36.9f))
                {
                    checkedAnchors = true;
                    for (var i = 0; i < 4; i++)
                    {
                        var side = i < 2 ? -1 : 1;
                        var anchor = i is 0 or 3 ? east * side * 2.5f : (north + east * side) * (2.5f / MathF.Sqrt(2));
                        Assert.That(Vector3.Distance(p.Member(dooms[i])!.Position, anchor), Is.LessThan(.1f));
                    }
                    foreach (var pair in scenario.LastState!.ChainPairs())
                        Assert.That((p.Member(pair.A)!.Position + p.Member(pair.B)!.Position).Length(), Is.LessThan(.1f));
                }
                if (p.Crossed(40.5f))
                {
                    checkedCleanse = true;
                    foreach (var role in dooms)
                        Assert.That(p.Member(role)!.HasStatus(DsrP5Constants.StatusId.Doom), Is.False);
                }
            },
        });
        Assert.That(checkedSpreads && checkedAnchors && checkedCleanse, Is.True);
        Assert.That(run.Passed, run.ToString);
        Assert.That(scenario.Completed, Is.True);
    }
}
