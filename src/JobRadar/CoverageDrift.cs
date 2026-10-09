namespace JobRadar;

/// <summary>
/// Conservative anomaly flag, not proof of a parser failure.
/// Triggers only on a 50%+ fall after a meaningful previous sample.
/// </summary>
public static class CoverageDrift
{
 public static string? Warning(int previous,int current)
 {
  if(previous<20 || current>=previous/2.0)return null;
  return $"Coverage anomaly: current {current} references vs previous {previous} (<50%); check source freshness, pagination and filters";
 }
}
