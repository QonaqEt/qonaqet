namespace Qonaqet.Application.Abstractions.Services;

/// <summary>
/// Öz QR encoder-imiz (ISO/IEC 18004, byte mode, ECC M). API modul matrisini qaytarır, klientlər onu özləri
/// çəkir — heç bir klientdə QR kitabxanası lazım deyil.
/// </summary>
public interface IQrCodeEncoder
{
    QrMatrix Encode(string text);
}

/// <summary>
/// Kvadrat modul matrisi (quiet zone daxil deyil; klient 4 modul boşluq əlavə etməlidir).
/// <see cref="Rows"/>-da hər sətir <c>'1'</c> (qara) və <c>'0'</c> (ağ) simvollarıdır.
/// </summary>
public sealed record QrMatrix(int Version, int Size, IReadOnlyList<string> Rows)
{
    public bool IsDark(int x, int y) => Rows[y][x] == '1';
}
