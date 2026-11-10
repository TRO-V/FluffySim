using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;
using static AnoMech.Scenarios.Dsr.DsrP5Constants;

namespace AnoMech.Scenarios.Dsr.P5Death;

public sealed class DsrP5DeathScenario : IScenario
{
    public string DisplayNameOverride => "P5 Death of the Heavens";
    public bool SupportsMultiplayer => true;
    public string Name => "Death of the Heavens (Elemental)";
    public IPhase Phase => DsrZone.P5;
    public bool IsIncomplete => true;
    public bool EnablePhaseBgm => true;
    public IReadOnlyList<IScenarioAi> AiStrats => [new DsrP5DeathAi(), new DsrP5DeathAi(doomsSouth: true)];
    private readonly DsrP5DeathSettingsWindow settings = new();
    public object SettingsOverrides => settings.Overrides;
    public void DrawSettings() => settings.Draw();
    public DsrP5DeathState? LastState { get; private set; }
    public bool Completed { get; private set; }
    public const float Death = 3f;
    public const float LightningLands = 24.17f;
    public const float SymbolsMarked = 32.05f;
    public const float Knockback = 37.23f;
    public static readonly float[] Earthquakes = [22.11f, 24.03f, 25.90f, 27.78f, 29.65f];
    private SimWorld world = null!;
    private DsrP5DeathState state = null!;
    private SimEnemy boss = null!;
    public TankSwapSequence? TankSwap { get; private set; }
    private SimEnemy warrior = null!;
    private SimEnemy quake = null!;
    private SimEnemy spear = null!;
    private SimEnemy twisterDragon = null!;
    private SimEnemy diveDragon = null!;
    private SimEnemy wings = null!;
    private SimEnemy mage = null!;
    private SimEnemy meteorKnight = null!;
    private SimEnemy knockbackKnight = null!;
    private SimEnemy gaze = null!;
    private SimEnemy eye = null!;
    private readonly List<SimEnemy> actors = [];
    private readonly Queue<SimEnemy> carriers = [];
    private readonly List<SimEnemy> meteors = [];
    private readonly List<(Vector3 At, SimEventObject? Obj)> twisters = [];
    private readonly List<(Vector3 At, SimEventObject? Obj)> puddles = [];
    private readonly List<(SimTether Tether, float Initial)> chains = [];
    private readonly HashSet<SimCharacter> chainBurned = [];
    private bool twistersArmed;
    private bool puddlesOpen;
    private bool failed;

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        TankSwap = null;
        world = worldParam; state = LastState = new(world.Rng, settings.Overrides, world.Party.PlayerRole);
        actors.Clear(); carriers.Clear(); meteors.Clear(); twisters.Clear(); puddles.Clear(); chains.Clear(); chainBurned.Clear();
        failed = false; Completed = false; twistersArmed = false; puddlesOpen = false;
        world.ActionResolved += OnAction;
        world.OnDespawn(() => { world.ActionResolved -= OnAction; world.Map.AddEffect(0x00040004, state.EyeSlot); });
        world.Events.Add(0, () =>
        {
            boss = Spawn(BNpcBaseId.DarkThordan, BNpcNameId.Thordan, Vector3.Zero, true);
            TankSwap = new(world, boss, ActionId.HeavenlyHeel, ActionId.AscalonsMight, StatusId.SlashingResistanceDown);
        });
        world.Events.Add(.5f, () => boss.SetTargetable(true));
        world.Events.Add(3f, () => boss.Cast(ActionId.DeathOfTheHeavens));
        world.Events.Add(10.04f, () =>
        {
            boss.SetTargetable(false); boss.SetTarget(null, false);
            boss.Cast(new EnemyAction(ActionId.ThordanLeap) { Cast = new() { CastSecondsOverride = 0 } }, state.ThordanSpot);
            world.Events.Add(.35f, () => boss.SetPosition(state.ThordanSpot));
        });
        world.Events.Add(11.60f, SpawnTrio);
        world.Events.Add(13.91f, Deathstorm);
        world.Events.Add(14.72f, () => { foreach (var role in state.Dooms) world.Party.Get(role)?.AddStatus(StatusId.Doom, 26f); });
        world.Events.Add(16.14f, () => warrior.Cast(ActionId.HeavyImpactWindup));
        world.Events.Add(16.14f, () => quake.Cast(new EnemyAction(ActionId.HeavyImpact)
        {
            Cast = new() { OmenDelay = 2.7f }, Effects = [Damage(Severity.Lethal)],
            Timing = new() { ResolveSnapshotOffset = .27f, DamageDelay = 0 },
        }));
        world.Events.Add(18.01f, StaticDives);
        for (var wave = 1; wave < 5; wave++)
        {
            var index = wave;
            world.Events.Add(Earthquakes[wave], () => quake.Cast(Instant(ActionId.HeavyImpact + (uint)index, Damage(Severity.Lethal)) with { Area = new() { Size = index * 6f } }));
        }
        world.Events.Add(23.99f, () => { foreach (var member in world.Party.ActiveMembers()) twisters.Add((member.Position, null)); });
        world.Events.Add(24.08f, SnapshotPuddles);
        world.Events.Add(24.17f, LightningStorm);
        world.Events.Add(25.08f, SpawnTwisters);
        world.Events.Add(25.68f, () => { foreach (var item in twisters) item.Obj?.EObjSetState(1, 0); });
        world.Events.Add(26.10f, () => twistersArmed = true);
        world.Events.Add(29.90f, ClearTwisters);
        world.Events.Add(27.81f, () => world.Map.AddEffect(0x00020001, state.EyeSlot));
        world.Events.Add(30.50f, SpawnPlaystation);
        world.Events.Add(32.05f, MarkSymbols);
        world.Events.Add(32.14f, () => { mage.Cast(ActionId.Heavensflame); boss.Cast(ActionId.DragonsGaze); });
        world.Events.Add(33.26f, () => { meteorKnight.Cast(ActionId.HolyMeteor); knockbackKnight.Cast(ActionId.FaithUnmoving); });
        world.Events.Add(34.30f, OpenPuddles);
        world.Events.Add(36.06f, TetherChains);
        world.Events.Add(37.22f, DragonsGaze);
        world.Events.Add(37.23f, () => knockbackKnight.Cast(Instant(ActionId.FaithUnmoving, Damage(), EnemyActionEffects.Knockback(169, 0))));
        world.Events.Add(38.16f, () => world.Map.AddEffect(0x00400020, state.EyeSlot));
        world.Events.Add(39.18f, ResolveChains);
        world.Events.Add(39.77f, Heavensflame);
        world.Events.Add(40.62f, DoomFalls);
        world.Events.Add(41.23f, SpawnMeteors);
        world.Events.Add(42f, BotLimitBreak);
        for (var at = 43f; at <= 47; at++) world.Events.Add(at, BotMeteorDamage);
        world.Events.Add(49f, ResolveMeteors);
        world.Events.Add(50f, () => { boss.SetPosition(Vector3.Zero); boss.SetTargetable(true); boss.Cast(Instant(ActionId.AncientQuaga, Damage()) with { Cast = new() { CastSecondsOverride = 5.7f } }); });
        world.Events.Add(61f, () => TankSwap!.Heel());
        foreach (var at in new[] { 68f, 69.65f, 71.30f }) world.Events.Add(at, () => TankSwap!.Cleave());
        world.Events.Add(74.4f, () => boss.Cast(Instant(ActionId.ThordanAttack, OnTarget(Damage()), OnOthers(Damage(Severity.Lethal))) with { Area = new() { Size = MathF.PI / 4 } }, world.Party.Get(TankSwap!.Holder)));
        world.Events.Add(78f, () => boss.Cast(27528));
        world.Events.Add(81f, () => { boss.CancelCast(CastCancelReason.Interrupted); boss.SetHealthFraction(.029f); boss.SetTargetable(false); });
        world.Events.Add(84f, () => { ClearTwisters(); foreach (var actor in actors) actor.Despawn(); foreach (var p in puddles) p.Obj?.Despawn(); Completed = !failed; });
        if (selectedAi is { } aiIndex) ((IScenarioAi<DsrP5DeathState>)AiStrats[aiIndex]).Run(state, world);
    }

    private static EnemyAction Instant(uint id, params IEnemyActionEffect[] effects) => new(id)
    { Cast = new() { CastSecondsOverride = 0 }, Effects = effects, Timing = new() { DamageDelay = 0 } };
    private SimEnemy Spawn(uint id, uint name, Vector3 at, bool listed = false)
    {
        var enemy = world.SpawnEnemy(DsrActors.WithNativeAppearance(new(id, name, Level: 90, Targetable: false,
            EnemyList: listed ? EnemyListMode.Always : EnemyListMode.Never, Placement: new(at, MathF.Atan2(-at.X, -at.Z))))) ?? throw new InvalidOperationException("DSR actor unavailable");
        actors.Add(enemy); return enemy;
    }
    private SimEnemy Helper(Vector3 at)
    {
        var enemy = world.SpawnEnemy(new(9020, Visibility: SpawnVisibility.InvisibleHelper, EnemyList: EnemyListMode.Never, Placement: new(at, 0))) ?? throw new InvalidOperationException("DSR carrier unavailable");
        actors.Add(enemy); return enemy;
    }
    private SimEnemy Carrier(Vector3 at)
    {
        var actor = carriers.Dequeue(); actor.SetPosition(at); return actor;
    }
    private void SpawnTrio()
    {
        warrior = Spawn(BNpcBaseId.Guerrique, BNpcNameId.Guerrique, state.WarriorSpot);
        spear = Spawn(BNpcBaseId.Zephirin, BNpcNameId.Zephirin, state.Outside(60, 23));
        twisterDragon = Spawn(BNpcBaseId.Vedrfolnir, BNpcNameId.Vedrfolnir, state.Outside(120, 23), listed: true);
        diveDragon = Spawn(BNpcBaseId.DarkscaleDiver, BNpcNameId.Darkscale, state.Outside(0, 23));
        wings = Spawn(BNpcBaseId.VidofnirDiver, BNpcNameId.Vidofnir, state.Outside(270, 23));
        quake = Helper(state.WarriorSpot); gaze = Helper(state.ThordanSpot); eye = Helper(state.EyeSpot);
        for (var i = 0; i < 48; i++) carriers.Enqueue(Helper(Vector3.Zero));
    }
    private void Deathstorm()
    {
        foreach (var role in state.Dooms) Carrier(world.Party.Get(role)!.Position).Cast(Instant(ActionId.Deathstorm), world.Party.Get(role));
    }
    private void StaticDives()
    {
        foreach (var (caster, id) in new[] { (spear, ActionId.DeathSpearOfTheFury), (twisterDragon, ActionId.TwistingDive), (diveDragon, ActionId.CauterizeDarkscale) })
            caster.Cast(new EnemyAction(id) { Effects = [Damage(Severity.Lethal)], Timing = new() { ResolveSnapshotOffset = .28f, DamageDelay = 0 } });
        wings.Cast(ActionId.WingsOfSalvation); boss.Cast(ActionId.LightningStorm);
    }
    private void SnapshotPuddles()
    {
        foreach (var role in Enum.GetValues<PartyRole>().Where(r => !state.IsDoomed(r)))
        {
            var at = world.Party.Get(role)!.Position; state.Puddles.Add(at);
            Carrier(at).Cast(new EnemyAction(ActionId.WingsOfSalvationPuddle));
        }
    }
    private void OpenPuddles()
    {
        foreach (var at in state.Puddles)
        {
            var obj = world.SpawnEventObject(new() { EObjId = EObjId.WingsOfSalvation, Placement = new(at, 0) });
            obj?.EObjSetState(1, 0); puddles.Add((at, obj));
        }
        puddlesOpen = true;
    }
    private void LightningStorm()
    {
        foreach (var member in world.Party.ActiveMembers().ToArray()) Carrier(member.Position).Cast(Instant(ActionId.LightningStormHit,
            Damage(), Resolve(ctx => { foreach (var hit in ctx.Hits) if (hit != member) ctx.Kill(hit, "overlapped lightning spreads"); })));
    }
    private void SpawnTwisters()
    {
        for (var i = 0; i < twisters.Count; i++) twisters[i] = (twisters[i].At, world.SpawnEventObject(new() { EObjId = EObjId.Twister, Placement = new(twisters[i].At, 0) }));
    }
    private void ClearTwisters()
    {
        twistersArmed = false; foreach (var item in twisters) item.Obj?.Despawn(); twisters.Clear();
    }
    private void SpawnPlaystation()
    {
        mage = Spawn(BNpcBaseId.Charibert, BNpcNameId.Charibert, state.Outside(90, 9));
        meteorKnight = Spawn(BNpcBaseId.Noudenet, BNpcNameId.Noudenet, state.Outside(180, 9));
        knockbackKnight = Spawn(BNpcBaseId.Grinnaux, BNpcNameId.Grinnaux, Vector3.Zero);
    }
    private void MarkSymbols()
    {
        state.HandOutSymbols(world.Party.ActiveMembers().OfType<ISimPartyMember>().ToDictionary(m => m.Role, m => ((SimCharacter)m).Position));
        foreach (var (role, symbol) in state.Symbols) world.Party.Get(role)?.AttachLockonVfx(symbol switch
        { ChainSymbol.Circle => LockonId.Circle, ChainSymbol.Triangle => LockonId.Triangle, ChainSymbol.Square => LockonId.Square, _ => LockonId.Cross });
    }
    private void TetherChains()
    {
        foreach (var (a, b, _) in state.ChainPairs())
        {
            var first = world.Party.Get(a)!; var second = world.Party.Get(b)!;
            chains.Add((world.Tether(first, second, TetherId.BurningChains, 15f, StatusId.BurningChains), Vector3.Distance(first.Position, second.Position)));
        }
    }
    private void DragonsGaze()
    {
        world.Map.AddEffect(0x00100001, state.EyeSlot);
        gaze.Cast(Instant(ActionId.DragonsGazeHit, Gaze(true)));
        eye.Cast(Instant(ActionId.DragonsGlory, Gaze(true)));
    }
    private void ResolveChains()
    {
        foreach (var chain in chains.Where(c => c.Tether.IsActive))
        {
            foreach (var holder in new[] { chain.Tether.A, chain.Tether.B }.OfType<SimCharacter>())
            {
                chainBurned.Add(holder);
                Carrier(holder.Position).Cast(Instant(ActionId.HolyChain, Damage(), ApplyStatus(StatusId.DamageDown, 180)) with { Area = new() { AdjustTargets = (_, _) => [holder] } });
            }
            chain.Tether.Despawn();
        }
    }
    private void Heavensflame()
    {
        var hitCounts = new Dictionary<SimCharacter, int>();
        foreach (var source in world.Party.ActiveMembers().ToArray()) Carrier(source.Position).Cast(Instant(ActionId.HeavensflameHit, Damage(),
            Resolve(ctx => { foreach (var hit in ctx.Hits) { hitCounts[hit] = hitCounts.GetValueOrDefault(hit) + 1; if (hitCounts[hit] > 1 || chainBurned.Contains(hit)) ctx.Kill(hit, "overlapped Heavensflame or unbroken chain"); } }), ApplyStatus(StatusId.FireResistanceDownII, 2.96f)));
    }
    private void DoomFalls()
    {
        foreach (var member in world.Party.ActiveMembers().Where(m => m.HasStatus(StatusId.Doom)).ToArray()) member.Die(ActionId.Deathstorm, "uncleansed Doom");
    }
    private void SpawnMeteors()
    {
        for (var i = 0; i < 8; i++) { var meteor = Spawn(BNpcBaseId.MeteorCircle, BNpcNameId.MeteorCircle, AtBearing(i * 45, 14), true); meteor.SetTargetable(true); meteors.Add(meteor); }
        world.Party.LimitBreak.Set(2);
    }
    private void BotLimitBreak()
    {
        var caster = world.Party.Get(PartyRole.CasterDps);
        var role = caster is { Dead: false } ? PartyRole.CasterDps : PartyRole.PhysRangedDps;
        if (world.Party.UseLimitBreak(role)) DestroyMeteorArc();
    }
    private void DestroyMeteorArc()
    {
        foreach (var meteor in meteors.Where(m => !m.Dead).Take(3)) meteor.Defeat();
    }
    private void BotMeteorDamage()
    {
        if (world.Party.ActiveMembers().Any(world.Party.IsBotDriven)) meteors.FirstOrDefault(m => !m.Dead)?.Defeat();
    }
    private void OnAction(SimCharacter actor, uint id, ulong target)
    {
        if (world.Events.Elapsed < 41.23f || world.Events.Elapsed > 49) return;
        if ((actor as ISimPartyMember)?.Role is PartyRole.CasterDps or PartyRole.PhysRangedDps && world.Party.LimitBreak.FilledBars == 2 && Core.Native.Interfaces.Natives.Data.Action(id)?.ActionCategory == 9) DestroyMeteorArc();
        else meteors.FirstOrDefault(m => (ulong)m.GameObjectId == target && !m.Dead)?.Defeat();
    }
    private void ResolveMeteors()
    {
        if (meteors.Any(m => !m.Dead)) { failed = true; world.Party.WipeAllPlayers(ActionId.HolyMeteor, "meteors survived their deadline"); }
    }
    public void Tick(float delta, float elapsed)
    {
        if (twistersArmed)
            foreach (var item in twisters.ToArray())
                if (world.Party.Find.InsideCircle(item.At, 1.5f).Any()) { Carrier(item.At).Cast(Instant(ActionId.Twister, Damage(Severity.Lethal))); item.Obj?.Despawn(); twisters.Remove(item); }
        if (puddlesOpen)
            foreach (var item in puddles.ToArray())
            {
                var member = world.Party.Find.InsideCircle(item.At, 4).FirstOrDefault(m => m.HasStatus(StatusId.Doom));
                if (member == null) continue;
                member.RemoveStatus(StatusId.Doom); item.Obj?.Despawn(); puddles.Remove(item);
            }
        foreach (var chain in chains.Where(c => c.Tether.IsActive && c.Tether.StretchGt(c.Initial + 30)).ToArray()) chain.Tether.Despawn();
    }
}
