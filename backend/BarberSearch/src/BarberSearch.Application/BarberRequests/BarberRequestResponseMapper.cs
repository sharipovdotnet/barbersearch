using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Domain.Users;

namespace BarberSearch.Application.BarberRequests;

/// <summary>
/// Преобразование агрегата <see cref="BarberRequest"/> в контракты ответов.
/// </summary>
/// <remarks>
/// Маппинг вынесен в один тип, потому что одни и те же поля собирают и обработчик создания,
/// и чтение своей заявки, и административные решения. Идентификаторы и статусы копируются
/// как есть; вложенный администраторский ответ добавляет аккаунт заявителя — единственное
/// место, где в контракт попадают PII.
/// </remarks>
public static class BarberRequestResponseMapper
{
    /// <summary>
    /// Собирает ответ заявителя.
    /// </summary>
    /// <param name="request">Заявка.</param>
    public static BarberRequestResponse Map(BarberRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new BarberRequestResponse(
            request.Id,
            request.Status,
            request.DisplayName.Value,
            request.YearsOfExperience.Value,
            request.City,
            request.About,
            request.HasSalon,
            request.CreatedAt,
            request.ReviewedAt,
            request.RejectionReason);
    }

    /// <summary>
    /// Собирает администраторский ответ: поля заявителя плюс аккаунт и автор решения.
    /// </summary>
    /// <remarks>
    /// Полное имя собирается из имени и фамилии; у аккаунтов домен требует непустые значения,
    /// но при пустом имени ответ не должен показывать пустую строку — тогда подставляется email.
    /// Проекция администраторского чтения передаёт те же поля заявителя, поэтому fallback
    /// остаётся здесь, а не дублируется в репозитории.
    /// </remarks>
    /// <param name="request">Заявка.</param>
    /// <param name="applicant">Аккаунт заявителя.</param>
    public static AdminBarberRequestResponse MapAdmin(BarberRequest request, User applicant)
    {
        ArgumentNullException.ThrowIfNull(applicant);

        return MapAdmin(
            request,
            applicant.Id,
            applicant.FirstName,
            applicant.LastName,
            applicant.Email);
    }

    /// <summary>
    /// Собирает администраторский ответ из заявки и полей аккаунта заявителя.
    /// </summary>
    /// <remarks>
    /// Перегрузка для проекции репозитория: он читает только идентификатор, имя, фамилию и email,
    /// а не весь агрегат <see cref="User"/>. Оба пути — решения администратора и чтение
    /// списка/карточки — заканчиваются этим методом, поэтому fallback имени один.
    /// </remarks>
    /// <param name="request">Заявка.</param>
    /// <param name="applicantId">Идентификатор аккаунта заявителя.</param>
    /// <param name="applicantFirstName">Имя аккаунта.</param>
    /// <param name="applicantLastName">Фамилия аккаунта.</param>
    /// <param name="applicantEmail">Email аккаунта.</param>
    public static AdminBarberRequestResponse MapAdmin(
        BarberRequest request,
        Guid applicantId,
        string applicantFirstName,
        string applicantLastName,
        string applicantEmail)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fullName = $"{applicantFirstName} {applicantLastName}".Trim();

        return new AdminBarberRequestResponse(
            request.Id,
            request.Status,
            request.DisplayName.Value,
            request.YearsOfExperience.Value,
            request.City,
            request.About,
            request.HasSalon,
            request.CreatedAt,
            request.ReviewedAt,
            request.RejectionReason,
            request.ReviewedByUserId,
            new AdminBarberRequestApplicantResponse(
                applicantId,
                string.IsNullOrWhiteSpace(fullName) ? applicantEmail : fullName,
                applicantEmail));
    }
}
