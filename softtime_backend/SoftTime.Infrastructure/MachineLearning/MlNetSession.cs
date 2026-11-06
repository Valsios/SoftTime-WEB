using Microsoft.ML;

namespace SoftTime.Infrastructure.MachineLearning;

/// <summary>Contexte ML.NET (graine fixe) et jeux d'entraînement et de test chargés.</summary>
internal sealed record MlNetSession(MLContext Ml, IDataView Train, IDataView Test)
{
    public static MlNetSession Create<TData>(IEnumerable<TData> train, IEnumerable<TData> test)
        where TData : class
    {
        var ml = new MLContext(seed: 1);
        return new MlNetSession(ml, ml.Data.LoadFromEnumerable(train), ml.Data.LoadFromEnumerable(test));
    }
}
