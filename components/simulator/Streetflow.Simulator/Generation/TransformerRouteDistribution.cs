using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Generation;

// Keep base transitions to unsuccessful exits when solving H; condition only afterward.
internal sealed class TransformerRouteDistribution
{
  private const double ProbabilityTolerance = 1e-9;
  private readonly TransformerRouteGraph graph;
  private readonly double[][] probabilities;
  private readonly double[] continuation;

  public TransformerRouteDistribution(TransformerRouteGraph graph, int destination, double[] scores, int offset)
  {
    this.graph = graph;
    probabilities = BaseProbabilities(scores, offset);
    continuation = SolveContinuation(destination);
  }

  public int Next(int state, Random random)
  {
    if (continuation[state] <= 0)
    {
      throw Error("Sampled routing state cannot complete its selected destination");
    }

    List<TransformerTransition> transitions = graph.States[state].Transitions;
    double total = 0;
    for (int index = 0; index < transitions.Count; index++)
    {
      total += probabilities[state][index] * continuation[transitions[index].NextState];
    }

    if (total <= 0 || !double.IsFinite(total))
    {
      throw Error("Destination-conditioned movement probabilities are numerically invalid");
    }

    double choice = random.NextDouble() * total;
    int last = -1;
    for (int index = 0; index < transitions.Count; index++)
    {
      double weight = probabilities[state][index] * continuation[transitions[index].NextState];
      if (weight <= 0)
      {
        continue;
      }

      last = transitions[index].NextState;
      choice -= weight;
      if (choice < 0)
      {
        return last;
      }
    }

    return last;
  }

  private double[][] BaseProbabilities(double[] scores, int offset)
  {
    double[][] result = new double[graph.States.Length][];
    for (int state = 0; state < graph.States.Length; state++)
    {
      List<TransformerTransition> transitions = graph.States[state].Transitions;
      result[state] = new double[transitions.Count];
      double maximum = double.NegativeInfinity;
      foreach (TransformerTransition transition in transitions)
      {
        maximum = Math.Max(maximum, scores[offset + transition.MovementIndex]);
      }

      double total = 0;
      for (int index = 0; index < transitions.Count; index++)
      {
        double weight = Math.Exp(scores[offset + transitions[index].MovementIndex] - maximum);
        if (weight <= 0 || !double.IsFinite(weight))
        {
          throw Error("Movement logits exceed the supported numerical range; a legal movement underflowed");
        }

        result[state][index] = weight;
        total += weight;
      }

      for (int index = 0; index < transitions.Count; index++)
      {
        result[state][index] /= total;
      }
    }

    return result;
  }

  private double[] SolveContinuation(int destination)
  {
    double[] result = new double[graph.States.Length];
    bool[] reachable = graph.Reachable(destination);
    List<int> transient = [];
    int[] indices = new int[graph.States.Length];
    Array.Fill(indices, -1);
    for (int state = 0; state < graph.States.Length; state++)
    {
      if (graph.IsDestination(state, destination))
      {
        result[state] = 1;
      }
      else if (reachable[state])
      {
        indices[state] = transient.Count;
        transient.Add(state);
      }
    }

    int count = transient.Count;
    double[,] system = new double[count, count + 1];
    for (int row = 0; row < count; row++)
    {
      int state = transient[row];
      system[row, row] = 1;
      List<TransformerTransition> transitions = graph.States[state].Transitions;
      for (int movement = 0; movement < transitions.Count; movement++)
      {
        int next = transitions[movement].NextState;
        double probability = probabilities[state][movement];
        if (indices[next] >= 0)
        {
          system[row, indices[next]] -= probability;
        }
        else
        {
          system[row, count] += probability * result[next];
        }
      }
    }

    Solve(system, count);
    for (int row = 0; row < count; row++)
    {
      double value = system[row, count];
      if (!double.IsFinite(value) || value <= 0 || value > 1 + ProbabilityTolerance)
      {
        throw Error("Destination continuation is numerically invalid; check movement-score strengths");
      }

      result[transient[row]] = Math.Min(1, value);
    }

    return result;
  }

  private static void Solve(double[,] system, int count)
  {
    for (int column = 0; column < count; column++)
    {
      int pivot = column;
      for (int row = column + 1; row < count; row++)
      {
        if (Math.Abs(system[row, column]) > Math.Abs(system[pivot, column]))
        {
          pivot = row;
        }
      }

      double divisor = system[pivot, column];
      if (divisor == 0 || !double.IsFinite(divisor))
      {
        throw Error("Destination-conditioning system is numerically singular");
      }

      for (int index = column; index <= count; index++)
      {
        double value = system[column, index];
        system[column, index] = system[pivot, index];
        system[pivot, index] = value;
      }

      for (int index = column; index <= count; index++)
      {
        system[column, index] /= divisor;
      }

      for (int row = 0; row < count; row++)
      {
        if (row == column)
        {
          continue;
        }

        double factor = system[row, column];
        for (int index = column; index <= count; index++)
        {
          system[row, index] -= factor * system[column, index];
        }
      }
    }
  }

  private static ApiException Error(string message) => new(400, "generation_error", message);
}
