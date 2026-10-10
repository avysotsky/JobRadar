using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class JsonlOutputTests
{
 [Fact]
 public void StandaloneJsonlContainsReadableCyrillicAndRemainsValidJson()
 {
  var json=JsonlOutput.Serialize(new {
   Title="Розробник .NET, віддалено",
   Description="Разработка серверных приложений",
   Note="Quoted \"text\" and a newline\nremain valid JSON"
  });
  Assert.Contains("Розробник",json);
  Assert.Contains("Разработка",json);
  Assert.DoesNotContain("\\u",json);
  using var parsed=JsonDocument.Parse(json);
  Assert.Equal("Розробник .NET, віддалено",
   parsed.RootElement.GetProperty("Title").GetString());
  Assert.Equal("Quoted \"text\" and a newline\nremain valid JSON",
   parsed.RootElement.GetProperty("Note").GetString());
  Assert.DoesNotContain('\n',json); // escaped newlines must not split a JSONL record
 }

 [Fact]
 public void CompactTriageKeepsReviewFieldsButNeverIncludesDescriptionOrPreview()
 {
  var vacancy=new VacancySnapshot(
   "dou","https://jobs.dou.ua/vacancies/111/","Middle .NET розробник",
   "Українська компанія","Very long preview "+new string('P',2000),
   "Very long description "+new string('D',10000),
   true,null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,["C#"]);
  var assessment=new VacancyAssessment(vacancy,WorkMode.Remote,FitBucket.LikelyFit,93,
    ["Підходить для віддаленої роботи"],["Актуальність не підтверджена"]);

  var json=JsonlOutput.Serialize(JsonlOutput.Compact(assessment));
  using var doc=JsonDocument.Parse(json);
  var root=doc.RootElement;
  Assert.Equal(11,root.EnumerateObject().Count());
  Assert.Equal("Middle .NET розробник",root.GetProperty("Title").GetString());
  Assert.Equal("LikelyFit",root.GetProperty("Bucket").GetString());
  Assert.Equal("Remote",root.GetProperty("WorkMode").GetString());
  Assert.Equal(93,root.GetProperty("Score").GetInt32());
  Assert.True(root.GetProperty("HasFullText").GetBoolean());
  Assert.Equal(JsonValueKind.Null,root.GetProperty("IsOpen").ValueKind);
  Assert.Contains("Підходить",json);
  Assert.DoesNotContain("\\u",json);
  Assert.False(root.TryGetProperty("Vacancy",out _));
  Assert.False(root.TryGetProperty("Description",out _));
  Assert.False(root.TryGetProperty("Preview",out _));
  Assert.DoesNotContain(new string('D',100),json);
 }
}
