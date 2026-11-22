using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Stabby.CodeGen
{
    public sealed class FloatRoundingPostProcessor : ILPostProcessor
    {
        static readonly HashSet<string> Targets = new HashSet<string> { "Stabby.World", "Stabby.Content" };

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly) => Targets.Contains(compiledAssembly.Name);

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            if (!WillProcess(compiledAssembly)) return null;
            var resolver = new DefaultAssemblyResolver();
            foreach (var dir in compiledAssembly.References.Select(Path.GetDirectoryName).Distinct())
                resolver.AddSearchDirectory(dir);
            var read = new ReaderParameters
            {
                SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData),
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                AssemblyResolver = resolver,
                ReadingMode = ReadingMode.Immediate,
                ReadSymbols = true,
            };
            using (var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(compiledAssembly.InMemoryAssembly.PeData), read))
            {
                FloatRounding.Apply(asm.MainModule);
                var pe = new MemoryStream();
                var pdb = new MemoryStream();
                asm.Write(pe, new WriterParameters { SymbolWriterProvider = new PortablePdbWriterProvider(), SymbolStream = pdb, WriteSymbols = true });
                return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), new List<DiagnosticMessage>());
            }
        }
    }
}
