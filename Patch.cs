using Mutagen.Bethesda.Pex;
using Noggog;

namespace PapyrusPatch;
public struct VarData
{
    public VariableType VariableType;
    public string? StringData;
    public int? IntData;
    public bool? BoolData;
    public float? FloatData;
    public PexObjectVariableData GetData()
    {
        return VariableType switch
        {
            VariableType.Null => new()
            {
                VariableType = VariableType.Null
            },
            VariableType.Identifier or VariableType.String => new()
            {
                VariableType = VariableType,
                StringValue = StringData,
            },
            VariableType.Integer => new()
            {
                VariableType = VariableType.Integer,
                IntValue = IntData,
            },
            VariableType.Bool => new()
            {
                VariableType = VariableType.Bool,
                BoolValue = BoolData
            },
            VariableType.Float => new()
            {
                VariableType = VariableType.Float,
                FloatValue = FloatData
            },
            _ => new()
            {
                VariableType = VariableType.Null
            },
        };
    }
}

public static class ListExts
{
    public static ExtendedList<PexObjectVariableData> GetData(this IEnumerable<VarData> data)
    {
        ExtendedList<PexObjectVariableData> dat = [];
        foreach (var inf in data)
        {
            dat.Add(inf.GetData());
        }
        return dat;
    }
    public static ExtendedList<PexObjectFunctionVariable> GetData(this IEnumerable<NewTemps> data)
    {
        ExtendedList<PexObjectFunctionVariable> dat = [];
        foreach (var inf in data)
        {
            dat.Add(inf.GetData());
        }
        return dat;
    }
}

struct IndexedVarData
{
    public int index;
    public VarData dat;
}

struct InstMatch
{
    public InstructionOpcode opCode;
    public IEnumerable<IndexedVarData> data;
    public readonly bool IsInst(PexObjectFunctionInstruction inst)
    {
        if(inst.Arguments.Count <= data.Max(x=>x.index)) return false;
        return data.All(x =>
        {
            var arg = inst.Arguments[x.index];
            if (arg.VariableType == x.dat.VariableType)
            {
                return arg.VariableType switch
                {
                    VariableType.Null => true,
                    VariableType.Identifier or VariableType.String => arg.StringValue == x.dat.StringData,
                    VariableType.Integer => arg.IntValue == x.dat.IntData,
                    VariableType.Bool => arg.BoolValue == x.dat.BoolData,
                    _ => true,
                };
            }
            else
            {
                return false;
            }
        });
    }
}

struct RewriteInstruction
{
    public InstMatch pred;
    public InstructionOpcode newInst;
    public IEnumerable<VarData> args;
}

struct RewriteArgs
{
    public InstMatch pred;
    public IEnumerable<IndexedVarData> args;
}

public struct NewTemps
{
    public string TypeName;
    public string Name;
    public PexObjectFunctionVariable GetData()
    {
        return new()
        {
            Name = Name,
            TypeName = TypeName,
        };
    }
}

struct InstructionList
{
    public InstructionOpcode opCode;
    public List<VarData> args;
}

struct InsertInstruction
{
    public InstMatch pred;
    public IEnumerable<InstructionList> instructions;
}

struct Patch
{
    public string FunctionName;
    public IEnumerable<NewTemps>? temps;
    public IEnumerable<InsertInstruction>? insert;
    public IEnumerable<RewriteInstruction>? rewrite;
    public IEnumerable<RewriteArgs>? rwArgs;
}

struct PatchState
{
    public string State;
    public string Obj;
    public List<Patch> funcPatch;
}

struct PatchFile
{
    public string FileName;
    public List<PatchState> states;
}
class Config
{
    public List<PatchFile> patches = [];
}