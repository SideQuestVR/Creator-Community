// MIT. Builds native Visual Scripting graphs; this Editor source is not a runtime dependency.
using System;
using Unity.VisualScripting;
using UnityEngine;
using BS;
using BS.VisualScripting;

public static class PortablePropGraphs
{
    public static T N<T>(FlowGraph g, T n) where T : Unit
    {
        g.units.Add(n);
        int i = g.units.Count - 1;
        n.position = new Vector2(i % 7 * 270, i / 7 * 220);
        return n;
    }
    public static ValueOutput V(FlowGraph g, string key)
    {
        var n = N(g, new GetVariable { kind = VariableKind.Object }); n.name.SetDefaultValue(key); return n.value;
    }
    public static ValueOutput L(FlowGraph g, object value) { return N(g, new Literal(value.GetType(), value)).output; }
    public static SetVariable Set(FlowGraph g, string key, ValueOutput value, ControlOutput enter = null)
    {
        var n = N(g, new SetVariable { kind = VariableKind.Object }); n.name.SetDefaultValue(key);
        value.ConnectToValid(n.input); if (enter != null) enter.ConnectToValid(n.assign); return n;
    }
    public static ValueOutput Read(FlowGraph g, Type type, string member, ValueOutput target = null)
    {
        var n = N(g, new GetMember(new Member(type, member))); if (target != null) target.ConnectToValid(n.target); return n.value;
    }
    public static ControlOutput Write(FlowGraph g, Type type, string member, ValueOutput target, ValueOutput value, ControlOutput enter)
    {
        var n = N(g, new SetMember(new Member(type, member))); target.ConnectToValid(n.target); value.ConnectToValid(n.input); enter.ConnectToValid(n.assign); return n.assigned;
    }
    public static If Gate(FlowGraph g, ControlOutput enter, ValueOutput condition)
    {
        var n = N(g, new If()); enter.ConnectToValid(n.enter); condition.ConnectToValid(n.condition); return n;
    }
    public static ValueOutput Eq(FlowGraph g, ValueOutput a, ValueOutput b)
    {
        var n = N(g, new Equal { numeric = false }); a.ConnectToValid(n.a); b.ConnectToValid(n.b); return n.comparison;
    }
    public static ValueOutput And(FlowGraph g, ValueOutput a, ValueOutput b)
    {
        var n = N(g, new And()); a.ConnectToValid(n.a); b.ConnectToValid(n.b); return n.result;
    }
    public static ValueOutput Greater(FlowGraph g, ValueOutput a, ValueOutput b)
    {
        var n = N(g, new Greater { numeric = true }); a.ConnectToValid(n.a); b.ConnectToValid(n.b); return n.comparison;
    }
    public static ControlOutput Active(FlowGraph g, ControlOutput enter, ValueOutput target, bool active)
    {
        var n = N(g, new InvokeMember(new Member(typeof(GameObject), "SetActive", new[] { typeof(bool) })));
        enter.ConnectToValid(n.enter); target.ConnectToValid(n.target); n.inputParameters[0].SetDefaultValue(active); return n.exit;
    }
    static ControlOutput Lit(FlowGraph g, ControlOutput enter, bool lit)
    {
        return Active(g, Set(g, "Lit", L(g, lit), enter).assigned, V(g, "FlameVisual"), lit);
    }

    public static FlowGraph Lighter()
    {
        var g = new FlowGraph { title = "Creator Lighter - held-hand trigger toggle" };
        var start = N(g, new Start());
        var begin = Set(g, "Held", L(g, false), start.trigger).assigned;
        begin = Set(g, "TriggerDown", L(g, false), begin).assigned; Lit(g, begin, false);
        var grab = N(g, new OnGrab()); V(g, "Grip").ConnectToValid(grab.banterHeldEvents);
        var got = Set(g, "Held", L(g, true), grab.trigger).assigned;
        got = Set(g, "HeldLeft", grab.isLeft, got).assigned; Set(g, "TriggerDown", L(g, false), got);
        var release = N(g, new OnRelease()); V(g, "Grip").ConnectToValid(release.banterHeldEvents);
        var relevant = Gate(g, release.trigger, Eq(g, release.isLeft, V(g, "HeldLeft")));
        var dropped = Set(g, "Held", L(g, false), relevant.ifTrue).assigned;
        dropped = Set(g, "TriggerDown", L(g, false), dropped).assigned; Lit(g, dropped, false);
        var trigger = N(g, new OnTrigger()); V(g, "Grip").ConnectToValid(trigger.banterHeldEvents);
        var eligible = Gate(g, trigger.trigger, And(g, V(g, "Held"), Eq(g, trigger.isLeft, V(g, "HeldLeft"))));
        var low = N(g, new Less { numeric = true }); trigger.input.ConnectToValid(low.a); low.b.SetDefaultValue(0.2f);
        var rearm = Gate(g, eligible.ifTrue, low.comparison); Set(g, "TriggerDown", L(g, false), rearm.ifTrue);
        var high = Greater(g, trigger.input, L(g, 0.55f));
        var press = Gate(g, rearm.ifFalse, And(g, high, Eq(g, V(g, "TriggerDown"), L(g, false))));
        var latched = Set(g, "TriggerDown", L(g, true), press.ifTrue).assigned;
        var toggle = Gate(g, latched, V(g, "Lit")); Lit(g, toggle.ifTrue, false);
        var turnedOn = Lit(g, toggle.ifFalse, true);
        var click = N(g, new InvokeMember(new Member(typeof(AudioSource), "Play", Type.EmptyTypes)));
        V(g, "Click").ConnectToValid(click.target); turnedOn.ConnectToValid(click.enter);
        return g;
    }

    public static FlowGraph Stick()
    {
        var g = new FlowGraph { title = "Creator Burning Stick - independent end ignition" };
        var start = N(g, new Start()); Lit(g, start.trigger, false);
        // Physics reports compound-trigger contacts to the Rigidbody root, so each tip
        // independently verifies distance from its own position to the other collider.
        var enter = N(g, new OnTriggerEnter()); var stay = N(g, new OnTriggerStay());
        V(g, "BodyObject").ConnectToValid(enter.target); V(g, "BodyObject").ConnectToValid(stay.target);
        foreach (var contact in new TriggerEventUnit[] { enter, stay })
        {
            var position = Read(g, typeof(Transform), "position", V(g, "Tip"));
            var closest = N(g, new InvokeMember(new Member(typeof(Collider), "ClosestPoint", new[] { typeof(Vector3) })));
            contact.collider.ConnectToValid(closest.target); position.ConnectToValid(closest.inputParameters[0]);
            var distance = N(g, new InvokeMember(new Member(typeof(Vector3), "Distance", new[] { typeof(Vector3), typeof(Vector3) })));
            position.ConnectToValid(distance.inputParameters[0]); closest.result.ConnectToValid(distance.inputParameters[1]);
            var close = N(g, new Less { numeric = true }); distance.result.ConnectToValid(close.a); close.b.SetDefaultValue(0.14f);
            var other = Read(g, typeof(Component), "gameObject", contact.collider);
            var name = Read(g, typeof(GameObject), "name", other);
            var named = N(g, new InvokeMember(new Member(typeof(string), "StartsWith", new[] { typeof(string), typeof(StringComparison) })));
            name.ConnectToValid(named.target); named.inputParameters[0].SetDefaultValue("Portable_Ignition_Source"); named.inputParameters[1].SetDefaultValue(StringComparison.Ordinal);
            var isSource = Gate(g, contact.trigger, And(g, close.comparison, named.result));
            var exists = N(g, new IsVariableDefined { kind = VariableKind.Object }); exists.name.SetDefaultValue("Lit"); other.ConnectToValid(exists.@object);
            var hasLit = Gate(g, isSource.ifTrue, exists.isVariableDefined);
            var otherLit = N(g, new GetVariable { kind = VariableKind.Object }); otherLit.name.SetDefaultValue("Lit"); other.ConnectToValid(otherLit.@object);
            var ignite = Gate(g, hasLit.ifTrue, And(g, otherLit.value, Eq(g, V(g, "Lit"), L(g, false))));
            var started = Set(g, "Remaining", V(g, "BurnSeconds"), ignite.ifTrue).assigned; Lit(g, started, true);
        }
        var update = N(g, new Update()); var burning = Gate(g, update.trigger, V(g, "Lit"));
        var subtract = N(g, new ScalarSubtract()); V(g, "Remaining").ConnectToValid(subtract.minuend);
        Read(g, typeof(Time), "deltaTime").ConnectToValid(subtract.subtrahend);
        var elapsed = Set(g, "Remaining", subtract.difference, burning.ifTrue).assigned;
        var ongoing = Gate(g, elapsed, Greater(g, V(g, "Remaining"), L(g, 0f))); Lit(g, ongoing.ifFalse, false);
        return g;
    }

    public static FlowGraph Radio()
    {
        var g = new FlowGraph { title = "Creator Radio - grab-aware idle and Finish return" };
        var start = N(g, new Start());
        var home = Set(g, "HomePosition", Read(g, typeof(Transform), "position", V(g, "Root")), start.trigger).assigned;
        home = Set(g, "HomeRotation", Read(g, typeof(Transform), "rotation", V(g, "Root")), home).assigned;
        var time = Read(g, typeof(Time), "time");
        Set(g, "LastActivity", time, home);
        var grab = N(g, new OnGrab()); V(g, "Grip").ConnectToValid(grab.banterHeldEvents);
        var left = Gate(g, grab.trigger, grab.isLeft);
        var leftOn = Set(g, "HeldLeft", L(g, true), left.ifTrue).assigned; Set(g, "LastActivity", time, leftOn);
        var rightOn = Set(g, "HeldRight", L(g, true), left.ifFalse).assigned; Set(g, "LastActivity", time, rightOn);
        var release = N(g, new OnRelease()); V(g, "Grip").ConnectToValid(release.banterHeldEvents);
        var releasedLeft = Gate(g, release.trigger, release.isLeft);
        var leftOff = Set(g, "HeldLeft", L(g, false), releasedLeft.ifTrue).assigned; Set(g, "LastActivity", time, leftOff);
        var rightOff = Set(g, "HeldRight", L(g, false), releasedLeft.ifFalse).assigned; Set(g, "LastActivity", time, rightOff);
        var own = N(g, new InvokeMember(new Member(typeof(BSSyncedObject), "_DoIOwn", Type.EmptyTypes))); V(g, "Sync").ConnectToValid(own.target);
        var update = N(g, new Update()); var owner = Gate(g, update.trigger, own.result);
        var held = N(g, new Or()); V(g, "HeldLeft").ConnectToValid(held.a); V(g, "HeldRight").ConnectToValid(held.b);
        var holding = Gate(g, owner.ifTrue, held.result); Set(g, "LastActivity", time, holding.ifTrue);
        var diff = N(g, new ScalarSubtract()); time.ConnectToValid(diff.minuend); V(g, "LastActivity").ConnectToValid(diff.subtrahend);
        var expired = Gate(g, holding.ifFalse, Greater(g, diff.difference, V(g, "ReturnAfterSeconds")));
        ReturnHome(g, expired.ifTrue, time);
        var contact = N(g, new OnTriggerEnter());
        var tag = N(g, new InvokeMember(new Member(typeof(Component), "CompareTag", new[] { typeof(string) })));
        contact.collider.ConnectToValid(tag.target); tag.inputParameters[0].SetDefaultValue("Finish");
        var finish = Gate(g, contact.trigger, And(g, tag.result, own.result)); ReturnHome(g, finish.ifTrue, time);
        return g;
    }
    static void ReturnHome(FlowGraph g, ControlOutput enter, ValueOutput time)
    {
        enter = Write(g, typeof(Transform), "position", V(g, "Root"), V(g, "HomePosition"), enter);
        enter = Write(g, typeof(Transform), "rotation", V(g, "Root"), V(g, "HomeRotation"), enter);
        enter = Write(g, typeof(Rigidbody), "linearVelocity", V(g, "Body"), L(g, Vector3.zero), enter);
        enter = Write(g, typeof(Rigidbody), "angularVelocity", V(g, "Body"), L(g, Vector3.zero), enter);
        Set(g, "LastActivity", time, enter);
    }
}
