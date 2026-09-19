using System.Security.Cryptography;
using SugarERP.Application;

namespace SugarERP.Sync.Client;

public sealed class EnrollmentClient(HttpClient httpClient) : IEnrollmentClient
{
    public async Task<EnrollmentResult> EnrollAsync(EnrollmentCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            return await new CentralApiClient(httpClient).EnrollAsync(command, cancellationToken);
        }
        catch (CentralApiException exception)
        {
            throw new BusinessRuleException(exception.Code, exception.Code switch
            {
                "ACTIVE_WRITER_EXISTS" => "هذا الفرع لديه جهاز تشغيل نشط بالفعل. ألغِ الجهاز القديم من لوحة الإدارة أولاً.",
                "UNAUTHENTICATED" => "رمز التسجيل غير صحيح أو انتهت صلاحيته. أنشئ رمزاً جديداً من لوحة الإدارة.",
                _ => "تعذر تسجيل الجهاز الآن. تحقق من الإنترنت وعنوان الخادم ثم حاول مرة أخرى."
            });
        }
    }

    public static string CreateKeyThumbprint()
    {
        var material = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexStringLower(SHA256.HashData(material));
    }

}
