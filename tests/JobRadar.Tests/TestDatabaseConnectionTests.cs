using Xunit;

namespace JobRadar.Tests;

public sealed class TestDatabaseConnectionTests
{
 [Fact]
 public void LegacyValueRemainsAvailableOnlyWhenSecretFileNotConfigured()
 {
  const string synthetic="Host=localhost;Database=FAKE_DB;Username=FAKE_USER;Password=FAKE_ONLY";
  Assert.Equal(synthetic,TestDatabaseConnection.ReadFromSources(null,synthetic));
 }

 [Fact]
 public void ReadsBoundedUtf8SecretFileWithoutPrintingCredentials()
 {
  var directory=Path.Combine(Path.GetTempPath(),"jobradar-secret-fixture-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(directory);
  var path=Path.Combine(directory,"connection");
  const string synthetic="Host=postgres;Database=jobradar_test;Username=fake;Password=FAKE_ONLY";
  try
  {
   File.WriteAllText(path,synthetic+"\n",new System.Text.UTF8Encoding(false));
   Assert.Equal(synthetic,TestDatabaseConnection.ReadFromSources(path,null));
  }
  finally{Directory.Delete(directory,true);}
 }

 [Fact]
 public void RejectsConflictingEnvironmentAndFileConfiguration()
 {
  var path=Path.Combine(Path.GetTempPath(),"synthetic-connection");
  Assert.Throws<InvalidOperationException>(()=>
   TestDatabaseConnection.ReadFromSources(path,"FAKE_ONLY"));
 }

 [Fact]
 public void RejectsRelativeSecretPath()
 {
  Assert.Throws<InvalidOperationException>(()=>
   TestDatabaseConnection.ReadFromSources("relative/connection",null));
 }

 [Theory]
 [InlineData("")]
 [InlineData("  ")]
 public void ConfiguredButEmptySecretFilePathFailsClosed(string path)
 {
  Assert.Throws<InvalidOperationException>(()=>
   TestDatabaseConnection.ReadFromSources(path,null));
 }

 [Fact]
 public void MissingSecretFileFailsClosed()
 {
  var path=Path.Combine(Path.GetTempPath(),
   "jobradar-secret-nonexistent-"+Guid.NewGuid().ToString("N"));
  Assert.Throws<InvalidOperationException>(()=>
   TestDatabaseConnection.ReadFromSources(path,null));
 }

 [Fact]
 public void DirectoryCannotBeUsedAsSecretFile()
 {
  var path=Path.Combine(Path.GetTempPath(),
   "jobradar-secret-dir-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(path);
  try
  {
   Assert.Throws<InvalidOperationException>(()=>
    TestDatabaseConnection.ReadFromSources(path,null));
  }
  finally{Directory.Delete(path);}
 }

 [Fact]
 public void RejectsOversizedSecret()
 {
  var path=Path.Combine(Path.GetTempPath(),"jobradar-oversize-"+Guid.NewGuid().ToString("N"));
  try
  {
   File.WriteAllText(path,new string('x',4097));
   Assert.Throws<InvalidOperationException>(()=>
    TestDatabaseConnection.ReadFromSources(path,null));
  }
  finally{File.Delete(path);}
 }

 [Fact]
 public void RejectsInvalidUtf8()
 {
  var path=Path.Combine(Path.GetTempPath(),"jobradar-invalid-utf8-"+Guid.NewGuid().ToString("N"));
  try
  {
   File.WriteAllBytes(path,new byte[]{0xff,0xfe});
   Assert.Throws<System.Text.DecoderFallbackException>(()=>
    TestDatabaseConnection.ReadFromSources(path,null));
  }
  finally{File.Delete(path);}
 }
}
