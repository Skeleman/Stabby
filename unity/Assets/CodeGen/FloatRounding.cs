using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Stabby.CodeGen
{
    public static class FloatRounding
    {
        enum K : byte { Other, F4, F8 }

        public static int Apply(ModuleDefinition module)
        {
            int added = 0;
            foreach (var type in module.GetTypes())
                foreach (var method in type.Methods)
                    if (method.HasBody) added += Apply(method);
            return added;
        }

        public static int Apply(MethodDefinition method)
        {
            var body = method.Body;
            body.SimplifyMacros();
            var targets = Analyse(method);
            var il = body.GetILProcessor();
            foreach (var ins in targets)
                if (ins.Next == null || ins.Next.OpCode.Code != Code.Conv_R4)
                    il.InsertAfter(ins, il.Create(OpCodes.Conv_R4));
            body.OptimizeMacros();
            return targets.Count;
        }

        static List<Instruction> Analyse(MethodDefinition method)
        {
            var body = method.Body;
            var result = new List<Instruction>();
            var marked = new HashSet<Instruction>();
            var entry = new Dictionary<Instruction, K[]>();
            var work = new Stack<Instruction>();
            void Push(Instruction at, K[] stack)
            {
                if (at == null || entry.ContainsKey(at)) return;
                entry[at] = stack;
                work.Push(at);
            }
            Push(body.Instructions[0], new K[0]);
            foreach (var h in body.ExceptionHandlers)
            {
                Push(h.HandlerStart, h.HandlerType == ExceptionHandlerType.Catch || h.HandlerType == ExceptionHandlerType.Filter ? new[] { K.Other } : new K[0]);
                if (h.FilterStart != null) Push(h.FilterStart, new[] { K.Other });
            }
            while (work.Count > 0)
            {
                var ins = work.Pop();
                var stack = new List<K>(entry[ins]);
                while (true)
                {
                    if (Step(method, ins, stack) && marked.Add(ins)) result.Add(ins);
                    var flow = ins.OpCode.FlowControl;
                    if (ins.Operand is Instruction t) Push(t, stack.ToArray());
                    else if (ins.Operand is Instruction[] ts) foreach (var x in ts) Push(x, stack.ToArray());
                    if (flow == FlowControl.Branch || flow == FlowControl.Return || flow == FlowControl.Throw) break;
                    ins = ins.Next;
                    if (ins == null) break;
                    if (entry.ContainsKey(ins)) break;
                    entry[ins] = stack.ToArray();
                }
            }
            result.Sort((a, b) => body.Instructions.IndexOf(a).CompareTo(body.Instructions.IndexOf(b)));
            return result;
        }

        static K Pop(List<K> s)
        {
            if (s.Count == 0) return K.Other;
            var k = s[s.Count - 1];
            s.RemoveAt(s.Count - 1);
            return k;
        }

        static void PopN(List<K> s, int n)
        {
            for (int i = 0; i < n; i++) Pop(s);
        }

        static K Of(TypeReference t)
        {
            if (t == null) return K.Other;
            switch (t.MetadataType)
            {
                case MetadataType.Single: return K.F4;
                case MetadataType.Double: return K.F8;
                default: return K.Other;
            }
        }

        static TypeReference Resolve(TypeReference t, MemberReference member)
        {
            if (t is GenericParameter gp)
            {
                if (gp.Type == GenericParameterType.Type && member.DeclaringType is GenericInstanceType git && gp.Position < git.GenericArguments.Count)
                    return git.GenericArguments[gp.Position];
                if (gp.Type == GenericParameterType.Method && member is GenericInstanceMethod gim && gp.Position < gim.GenericArguments.Count)
                    return gim.GenericArguments[gp.Position];
            }
            if (t is ByReferenceType) return null;
            return t;
        }

        static bool Step(MethodDefinition method, Instruction ins, List<K> s)
        {
            switch (ins.OpCode.Code)
            {
                case Code.Add: case Code.Sub: case Code.Mul: case Code.Div: case Code.Rem:
                {
                    var b = Pop(s); var a = Pop(s);
                    var k = a == K.F8 || b == K.F8 ? K.F8 : a == K.F4 || b == K.F4 ? K.F4 : K.Other;
                    s.Add(k);
                    return k == K.F4;
                }
                case Code.Neg:
                {
                    var a = Pop(s);
                    s.Add(a);
                    return a == K.F4;
                }
                case Code.Dup:
                {
                    var a = Pop(s);
                    s.Add(a); s.Add(a);
                    return false;
                }
                case Code.Ldc_R4: case Code.Conv_R4: case Code.Ldind_R4: case Code.Ldelem_R4:
                    PopN(s, Pops(ins)); s.Add(K.F4); return false;
                case Code.Ldc_R8: case Code.Conv_R8: case Code.Conv_R_Un: case Code.Ldind_R8: case Code.Ldelem_R8:
                    PopN(s, Pops(ins)); s.Add(K.F8); return false;
                case Code.Ldloc:
                    s.Add(Of(((VariableDefinition)ins.Operand).VariableType)); return false;
                case Code.Ldarg:
                    s.Add(Of(((ParameterDefinition)ins.Operand).ParameterType)); return false;
                case Code.Ldfld:
                    Pop(s); s.Add(Of(Resolve(((FieldReference)ins.Operand).FieldType, (FieldReference)ins.Operand))); return false;
                case Code.Ldsfld:
                    s.Add(Of(Resolve(((FieldReference)ins.Operand).FieldType, (FieldReference)ins.Operand))); return false;
                case Code.Ldelem_Any: case Code.Ldobj:
                    PopN(s, Pops(ins)); s.Add(Of((TypeReference)ins.Operand)); return false;
                case Code.Unbox_Any:
                    Pop(s); s.Add(Of((TypeReference)ins.Operand)); return false;
                case Code.Call: case Code.Callvirt: case Code.Newobj:
                {
                    var m = (MethodReference)ins.Operand;
                    int n = m.Parameters.Count + (m.HasThis && ins.OpCode.Code != Code.Newobj ? 1 : 0);
                    PopN(s, n);
                    if (ins.OpCode.Code == Code.Newobj) s.Add(K.Other);
                    else if (m.ReturnType.MetadataType != MetadataType.Void) s.Add(Of(Resolve(m.ReturnType, m)));
                    return false;
                }
                case Code.Calli:
                {
                    var cs = (CallSite)ins.Operand;
                    PopN(s, cs.Parameters.Count + (cs.HasThis ? 1 : 0) + 1);
                    if (cs.ReturnType.MetadataType != MetadataType.Void) s.Add(Of(cs.ReturnType));
                    return false;
                }
                case Code.Ret:
                    if (method.ReturnType.MetadataType != MetadataType.Void) Pop(s);
                    return false;
                case Code.Leave: case Code.Endfinally:
                    s.Clear(); return false;
            }
            PopN(s, Pops(ins));
            for (int i = Pushes(ins); i > 0; i--) s.Add(K.Other);
            return false;
        }

        static int Pops(Instruction ins)
        {
            switch (ins.OpCode.StackBehaviourPop)
            {
                case StackBehaviour.Pop0: return 0;
                case StackBehaviour.Pop1: case StackBehaviour.Popi: case StackBehaviour.Popref: return 1;
                case StackBehaviour.Pop1_pop1: case StackBehaviour.Popi_pop1: case StackBehaviour.Popi_popi:
                case StackBehaviour.Popi_popi8: case StackBehaviour.Popi_popr4: case StackBehaviour.Popi_popr8:
                case StackBehaviour.Popref_pop1: case StackBehaviour.Popref_popi: return 2;
                case StackBehaviour.Popi_popi_popi: case StackBehaviour.Popref_popi_popi: case StackBehaviour.Popref_popi_popi8:
                case StackBehaviour.Popref_popi_popr4: case StackBehaviour.Popref_popi_popr8: case StackBehaviour.Popref_popi_popref: return 3;
                default: return 0;
            }
        }

        static int Pushes(Instruction ins)
        {
            switch (ins.OpCode.StackBehaviourPush)
            {
                case StackBehaviour.Push0: return 0;
                case StackBehaviour.Push1_push1: return 2;
                default: return 1;
            }
        }
    }
}
