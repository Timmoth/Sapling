using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using System.Text;

namespace Sapling.SourceGenerators
{

    [Generator]
    public sealed class ExampleGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
           


            context.RegisterPostInitializationOutput(ctx =>
            {
                ctx.AddSource("ExampleGenerator.g", SourceText.From(source, Encoding.UTF8));
            });
        }
    }


}
