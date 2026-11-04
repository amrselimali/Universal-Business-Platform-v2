using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Sales;

public partial class CustomerSessionDialog : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public IReadOnlyList<Branch> Branches { get; set; } = Array.Empty<Branch>();
    [Parameter] public Guid? DefaultBranchId { get; set; }
    [Parameter] public Guid? DefaultCustomerId { get; set; }
    [Parameter] public string? DefaultCustomerName { get; set; }
    [Parameter] public DateTime DefaultDate { get; set; } = DateTime.Today;

    [Inject] private BookingService Bookings { get; set; } = default!;
    [Inject] private PartnerService Partners { get; set; } = default!;
    [Inject] private MasterDataService MasterData { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private Guid _branchId;
    private Guid _customerId;
    private Guid? _employeeId;
    private DateTime? _date;
    private TimeSpan? _start = new(10, 0, 0);
    private TimeSpan? _end = new(10, 30, 0);
    private string? _notes;
    private IReadOnlyList<Partner> _customers = Array.Empty<Partner>();
    private IReadOnlyList<Employee> _employees = Array.Empty<Employee>();
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        _branchId = DefaultBranchId ?? Branches.FirstOrDefault()?.Id ?? Guid.Empty;
        _date = DefaultDate;
        _customerId = DefaultCustomerId ?? Guid.Empty;

        var actor = await UserContext.GetAsync();
        if (actor is null) return;

        _employees = await MasterData.EmployeeOptionsAsync(actor);
        var customers = (await Partners.ListAsync(actor, PartnerKind.Customer,
            new PageRequest { Page = 1, PageSize = 100 }, null)).Items.ToList();
        if (_customerId != Guid.Empty && customers.All(c => c.Id != _customerId))
        {
            // العميل القادم من الشاشة قد لا يكون في أول صفحة البحث — نضيفه للعرض فقط.
            customers.Insert(0, new Partner
            {
                Id = _customerId, Code = "", NameAr = DefaultCustomerName ?? ""
            });
        }
        _customers = customers;
    }

    private void OnBranchChanged(Guid branchId) => _branchId = branchId;

    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        if (_branchId == Guid.Empty || _customerId == Guid.Empty || _date is null ||
            _start is null || _end is null)
        {
            Snackbar.Add(T.T("validation.required_fields"), Severity.Warning);
            return;
        }
        if (_end <= _start)
        {
            Snackbar.Add(T.T("validation.booking_time_range"), Severity.Warning);
            return;
        }

        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            var booking = await Bookings.CreateSessionAsync(actor, new BookingInput
            {
                BranchId = _branchId, CustomerId = _customerId, BookingDate = _date.Value.Date,
                StartsAt = new DateTimeOffset(_date.Value.Date + _start.Value),
                EndsAt = new DateTimeOffset(_date.Value.Date + _end.Value),
                AssignedEmployeeId = _employeeId,
                Notes = string.IsNullOrWhiteSpace(_notes) ? null : _notes.Trim()
            });
            MudDialog.Close(DialogResult.Ok(booking));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
