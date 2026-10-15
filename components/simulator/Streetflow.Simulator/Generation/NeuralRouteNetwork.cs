using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace Streetflow.Simulator.Generation;

public sealed class NeuralRouteNetwork : IDisposable
{
  private readonly InferenceSession session;
  private readonly NeuralModel model;

  public NeuralRouteNetwork(string path, NeuralModel model)
  {
    this.model = model;
    using SessionOptions settings = new() { IntraOpNumThreads = 1, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
    session = new InferenceSession(File.ReadAllBytes(path), settings);
    try
    {
      ValidateSignature();
      float[] hidden = new float[model.HiddenSize];
      _ = Step(0, model.MaxTurns + 1, 0, ref hidden);
      hidden = new float[model.HiddenSize];
      _ = Step(model.Roads.Length - 1, model.MaxTurns, model.Periods.Length - 1, ref hidden);
    }
    catch
    {
      session.Dispose();
      throw;
    }
  }

  public float[] Step(int road, int previousTurn, int period, ref float[] hidden)
  {
    NamedOnnxValue[] inputs =
    [
      NamedOnnxValue.CreateFromTensor("road", new DenseTensor<long>(new long[] { road }, new int[] { 1 })),
      NamedOnnxValue.CreateFromTensor("previous_turn", new DenseTensor<long>(new long[] { previousTurn }, new int[] { 1 })),
      NamedOnnxValue.CreateFromTensor("period", new DenseTensor<long>(new long[] { period }, new int[] { 1 })),
      NamedOnnxValue.CreateFromTensor("hidden", new DenseTensor<float>(hidden, new int[] { 1, model.HiddenSize }))
    ];
    using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run(inputs);
    float[]? logits = null;
    foreach (DisposableNamedOnnxValue output in outputs)
    {
      Tensor<float> tensor = output.AsTensor<float>();
      float[] values = new float[tensor.Length];
      int index = 0;
      foreach (float value in tensor)
      {
        values[index++] = value;
      }
      foreach (float value in values)
      {
        if (!float.IsFinite(value))
        {
          throw new ApiException(400, "generation_error", "Neural route model produced nonfinite values");
        }
      }

      if (output.Name == "logits")
      {
        logits = values;
      }
      else if (output.Name == "next_hidden")
      {
        hidden = values;
      }
    }

    return logits ?? throw new ApiException(400, "generation_error", "Neural route model has no logits output");
  }

  private void ValidateSignature()
  {
    Require(session.InputMetadata.Count == 4 && session.OutputMetadata.Count == 2);
    ValidateNode(session.InputMetadata, "road", typeof(long), 1);
    ValidateNode(session.InputMetadata, "previous_turn", typeof(long), 1);
    ValidateNode(session.InputMetadata, "period", typeof(long), 1);
    ValidateNode(session.InputMetadata, "hidden", typeof(float), 1, model.HiddenSize);
    ValidateNode(session.OutputMetadata, "logits", typeof(float), 1, model.MaxTurns + 1);
    ValidateNode(session.OutputMetadata, "next_hidden", typeof(float), 1, model.HiddenSize);
  }

  private static void ValidateNode(IReadOnlyDictionary<string, NodeMetadata> nodes, string name, Type type, params int[] dimensions)
  {
    Require(nodes.TryGetValue(name, out NodeMetadata? node));
    Require(node!.IsTensor && node.ElementType == type && node.Dimensions.Length == dimensions.Length);
    for (int index = 0; index < dimensions.Length; index++)
    {
      Require(node.Dimensions[index] == dimensions[index]);
    }
  }

  private static void Require(bool condition)
  {
    if (!condition)
    {
      throw ProjectPaths.Invalid("Neural model ONNX signature does not match local_turn_gru_v1 metadata");
    }
  }

  public void Dispose() => session.Dispose();
}
