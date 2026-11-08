// Canonical semantic IL comparison. Metadata tokens and short/long encodings
// may differ after a loader's accessibility rewrite; resolved members, constants,
// branches, local types and exception regions must still match exactly.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;

namespace StrongholdEuropeDE.Runtime
{
    internal static class CanonicalManagedIL
    {
        private sealed class Instruction
        {
            internal int Offset;
            internal OpCode Op;
            internal object Operand;
        }

        private static readonly Dictionary<ushort, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => unchecked((ushort)o.Value), o => o);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string TypeName(Type t)
        {
            if (t == null)
                return "void";
            if (t.IsByRef)
                return TypeName(t.GetElementType()) + "&";
            if (t.IsPointer)
                return TypeName(t.GetElementType()) + "*";
            if (t.IsArray)
                return TypeName(t.GetElementType()) + "[" + new string (',', t.GetArrayRank() - 1) + "]";
            if (t.IsGenericParameter)
                return (t.DeclaringMethod == null ? "!" : "!!") + t.GenericParameterPosition.ToString(Inv);
            if (t.IsGenericType)
                return (t.GetGenericTypeDefinition().FullName ?? t.Name) + "<" + string.Join(",", t.GetGenericArguments().Select(TypeName)) + ">";
            return t.FullName ?? t.Name;
        }

        private static string MemberName(MemberInfo member)
        {
            var type = member as Type;
            if (type != null)
                return "T:" + TypeName(type);
            var field = member as FieldInfo;
            if (field != null)
                return "F:" + TypeName(field.DeclaringType) + "::" + field.Name + ":" + TypeName(field.FieldType);
            var method = member as MethodBase;
            if (method != null)
            {
                var info = method as MethodInfo;
                string generic = method.IsGenericMethod ? "<" + string.Join(",", method.GetGenericArguments().Select(TypeName)) + ">" : "";
                return "M:" + TypeName(method.DeclaringType) + "::" + method.Name + generic + "(" + string.Join(",", method.GetParameters().Select(p => TypeName(p.ParameterType))) + ")=>" + (info == null ? "System.Void" : TypeName(info.ReturnType)) + ":" + (method.IsStatic ? "static" : "instance");
            }

            throw new InvalidDataException("Unsupported runtime IL member");
        }

        private static int TakeInt(byte[] bytes, ref int p)
        {
            if (p > bytes.Length - 4)
                throw new EndOfStreamException();
            int v = BitConverter.ToInt32(bytes, p);
            p += 4;
            return v;
        }

        private static byte TakeByte(byte[] bytes, ref int p)
        {
            if (p >= bytes.Length)
                throw new EndOfStreamException();
            return bytes[p++];
        }

        private static ushort TakeUShort(byte[] bytes, ref int p)
        {
            if (p > bytes.Length - 2)
                throw new EndOfStreamException();
            ushort v = BitConverter.ToUInt16(bytes, p);
            p += 2;
            return v;
        }

        internal static string Normalize(MethodBase method)
        {
            if (method == null)
                throw new ArgumentNullException("method");
            var body = method.GetMethodBody();
            if (body == null)
                throw new InvalidDataException("Method body unavailable");
            byte[] bytes = body.GetILAsByteArray();
            if (bytes == null || bytes.Length == 0 || bytes.Length > 1000000)
                throw new InvalidDataException("Method IL size");
            var module = method.Module;
            Type[] typeArgs = method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : Type.EmptyTypes;
            Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
            var list = new List<Instruction>();
            int pos = 0;
            while (pos < bytes.Length)
            {
                int at = pos;
                ushort code = TakeByte(bytes, ref pos);
                if (code == 0xfe)
                    code = (ushort)(0xfe00 | TakeByte(bytes, ref pos));
                OpCode op;
                if (!Codes.TryGetValue(code, out op))
                    throw new InvalidDataException("Unknown IL opcode");
                object arg = null;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineI:
                        arg = (int)unchecked((sbyte)TakeByte(bytes, ref pos));
                        break;
                    case OperandType.InlineI:
                        arg = TakeInt(bytes, ref pos);
                        break;
                    case OperandType.InlineI8:
                        if (pos > bytes.Length - 8)
                            throw new EndOfStreamException();
                        arg = BitConverter.ToInt64(bytes, pos);
                        pos += 8;
                        break;
                    case OperandType.ShortInlineR:
                        arg = "floatbits:" + unchecked((uint)TakeInt(bytes, ref pos)).ToString("x8", Inv);
                        break;
                    case OperandType.InlineR:
                        if (pos > bytes.Length - 8)
                            throw new EndOfStreamException();
                        arg = "doublebits:" + BitConverter.ToUInt64(bytes, pos).ToString("x16", Inv);
                        pos += 8;
                        break;
                    case OperandType.ShortInlineVar:
                        arg = (int)TakeByte(bytes, ref pos);
                        break;
                    case OperandType.InlineVar:
                        arg = (int)TakeUShort(bytes, ref pos);
                        break;
                    case OperandType.ShortInlineBrTarget:
                    {
                        int delta = unchecked((sbyte)TakeByte(bytes, ref pos));
                        arg = checked(pos + delta);
                        break;
                    }

                    case OperandType.InlineBrTarget:
                    {
                        int delta = TakeInt(bytes, ref pos);
                        arg = checked(pos + delta);
                        break;
                    }

                    case OperandType.InlineSwitch:
                    {
                        int count = TakeInt(bytes, ref pos);
                        if (count < 0 || count > bytes.Length / 4 || pos > bytes.Length - count * 4)
                            throw new InvalidDataException("IL switch size");
                        int end = checked(pos + count * 4);
                        var labels = new int[count];
                        for (int i = 0; i < count; i++)
                            labels[i] = checked(end + TakeInt(bytes, ref pos));
                        arg = labels;
                        break;
                    }

                    case OperandType.InlineString:
                        arg = "string:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(module.ResolveString(TakeInt(bytes, ref pos))));
                        break;
                    case OperandType.InlineField:
                        arg = MemberName(module.ResolveField(TakeInt(bytes, ref pos), typeArgs, methodArgs));
                        break;
                    case OperandType.InlineMethod:
                        arg = MemberName(module.ResolveMethod(TakeInt(bytes, ref pos), typeArgs, methodArgs));
                        break;
                    case OperandType.InlineType:
                        arg = MemberName(module.ResolveType(TakeInt(bytes, ref pos), typeArgs, methodArgs));
                        break;
                    case OperandType.InlineTok:
                        arg = MemberName(module.ResolveMember(TakeInt(bytes, ref pos), typeArgs, methodArgs));
                        break;
                    case OperandType.InlineSig:
                        throw new InvalidDataException("Indirect call signatures require separate ABI attestation");
                    default:
                        throw new InvalidDataException("Unsupported IL operand");
                }

                list.Add(new Instruction { Offset = at, Op = op, Operand = arg });
            }

            var labelsByOffset = new Dictionary<int, int>();
            for (int i = 0; i < list.Count; i++)
                labelsByOffset.Add(list[i].Offset, i);
            labelsByOffset.Add(bytes.Length, list.Count);
            Func<int, string> label = offset =>
            {
                int n;
                if (!labelsByOffset.TryGetValue(offset, out n))
                    throw new InvalidDataException("IL branch target not an instruction");
                return "L" + n.ToString(Inv);
            };
            var text = new StringBuilder();
            text.Append("SELF:").Append(MemberName(method)).Append('\n');
            text.Append("INIT:").Append(body.InitLocals ? "1" : "0").Append('\n');
            foreach (var local in body.LocalVariables.OrderBy(l => l.LocalIndex))
                text.Append("LOCAL:").Append(local.LocalIndex.ToString(Inv)).Append(':').Append(TypeName(local.LocalType)).Append(':').Append(local.IsPinned ? "1" : "0").Append('\n');
            foreach (var i in list)
            {
                string name = i.Op.Name;
                object arg = i.Operand;
                if (name.StartsWith("ldc.i4.", StringComparison.Ordinal))
                {
                    string suffix = name.Substring(7);
                    if (suffix == "m1")
                    {
                        name = "ldc.i4";
                        arg = -1;
                    }
                    else if (suffix == "s")
                        name = "ldc.i4";
                    else
                    {
                        int n;
                        if (int.TryParse(suffix, NumberStyles.None, Inv, out n))
                        {
                            name = "ldc.i4";
                            arg = n;
                        }
                    }
                }

                foreach (string kind in new[]
                {
                    "ldarg",
                    "ldarga",
                    "starg",
                    "ldloc",
                    "ldloca",
                    "stloc"
                }

                )
                {
                    if (name == kind + ".s")
                        name = kind;
                    else if (name.StartsWith(kind + ".", StringComparison.Ordinal))
                    {
                        int n;
                        if (int.TryParse(name.Substring(kind.Length + 1), NumberStyles.None, Inv, out n))
                        {
                            name = kind;
                            arg = n;
                        }
                    }
                }

                if (i.Op.OperandType == OperandType.InlineBrTarget || i.Op.OperandType == OperandType.ShortInlineBrTarget)
                {
                    if (name.EndsWith(".s", StringComparison.Ordinal))
                        name = name.Substring(0, name.Length - 2);
                    arg = label((int)i.Operand);
                }

                if (i.Op.OperandType == OperandType.InlineSwitch)
                    arg = string.Join(",", ((int[])i.Operand).Select(label));
                text.Append(name).Append('|').Append(arg == null ? "" : Convert.ToString(arg, Inv)).Append('\n');
            }

            foreach (var clause in body.ExceptionHandlingClauses)
            {
                text.Append("EH:").Append(((int)clause.Flags).ToString(Inv)).Append(':').Append(label(clause.TryOffset)).Append('-').Append(label(clause.TryOffset + clause.TryLength)).Append(':').Append(label(clause.HandlerOffset)).Append('-').Append(label(clause.HandlerOffset + clause.HandlerLength)).Append(':');
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause)
                    text.Append(TypeName(clause.CatchType));
                else if (clause.Flags == ExceptionHandlingClauseOptions.Filter)
                    text.Append(label(clause.FilterOffset));
                text.Append('\n');
            }

            return text.ToString();
        }

        internal static string Digest(MethodBase method)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Normalize(method)))).Replace("-", "").ToLowerInvariant();
        }
    }
}
