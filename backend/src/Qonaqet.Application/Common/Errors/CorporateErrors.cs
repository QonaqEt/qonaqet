using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>Korporativ hesabların və sifarişlərin sabit xəta kodları.</summary>
public static class CorporateErrors
{
    public static readonly Error NotFound = Error.NotFound("corporate_not_found", "Şirkət tapılmadı.");

    public static readonly Error Invalid = Error.Validation("corporate_invalid", "Şirkətin adı, VÖEN-i (10 rəqəm), email-i və brend rəngi (#RRGGBB) düzgün olmalıdır.");

    public static readonly Error VoenTaken = Error.Conflict("corporate_voen_taken", "Bu VÖEN ilə şirkət artıq var.");

    public static readonly Error UserNotFound = Error.NotFound("user_not_found", "Bu email ilə QONAQET hesabı yoxdur.");

    /// <summary>Cari istifadəçi heç bir şirkətə bağlı deyil.</summary>
    public static readonly Error NoCorporate = Error.Forbidden("corporate_access_denied", "Hesabınız şirkətə bağlı deyil.");

    public static readonly Error OrderNotFound = Error.NotFound("corporate_order_not_found", "Sifariş tapılmadı.");

    public static readonly Error OrderNotEditable = Error.Conflict("corporate_order_not_editable", "Sifariş artıq ödənişə göndərilib və dəyişdirilə bilməz.");

    public static readonly Error RecipientsRequired = Error.Conflict("corporate_recipients_required", "Əvvəlcə alıcı siyahısını yükləyin.");

    public static readonly Error CsvInvalid = Error.Validation("corporate_csv_invalid", "CSV faylı oxuna bilmədi: başlıq name,email olmalıdır.");

    public static readonly Error BankTransferRequiresDonation =
        Error.Validation("corporate_bank_transfer_requires_donation", "Bank köçürməsində istifadə olunmayan dəyər yalnız hovuza bağışlana bilər.");

    public static readonly Error NotAwaitingBankTransfer =
        Error.Conflict("corporate_order_not_awaiting_bank_transfer", "Sifariş bank köçürməsi gözləmir.");

    public static readonly Error NotCancellable = Error.Conflict("corporate_order_not_cancellable", "Ödənilmiş sifariş ləğv edilə bilməz.");

    public static readonly Error ReportNotFound = Error.NotFound("corporate_report_not_found", "Hesabat tapılmadı.");

    public static readonly Error ReportNotReady = Error.Conflict("corporate_report_not_ready", "Hesabat hələ hazırlanır.");
}
