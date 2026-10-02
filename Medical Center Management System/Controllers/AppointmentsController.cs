using Medical_Center_Management_System.Models;
using Medical_Center_Management_System.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Medical_Center_Management_System.Controllers
{
    [Authorize(Roles = "Admin")]
  
    public class AppointmentsController : Controller
    {
        private readonly AppDbContext _context;

        public AppointmentsController(AppDbContext context)
        {
            _context = context;
        }
        private const int SlotMinutes = 30;

        public async Task<IActionResult> AvailableSlots(
                   DateTime? date,
                   int? clinicId,
                   string? search)
        {
            var targetDate = date?.Date ?? DateTime.Today;

            ViewBag.SelectedDate = targetDate;
            ViewBag.SelectedClinic = clinicId;

            var tomorrow = targetDate.AddDays(1);

            // Appointments already booked in selected day
            var bookedToday = await _context.Appointments
                .Where(a =>
                    a.AppointmentDate >= targetDate &&
                    a.AppointmentDate < tomorrow &&
                    a.Status != "Cancelled")
                .ToListAsync();

            // Clinics for filter dropdown
            var clinics = await _context.Clinics
                .OrderBy(c => c.Name)
                .ToListAsync();

            ViewBag.Clinics = clinics;

            // Doctors query
            var doctorsQuery = _context.Doctors
                .Include(d => d.Clinic)
                .Include(d => d.Specialty)
                .AsQueryable();

            // Filter by clinic
            if (clinicId.HasValue)
            {
                doctorsQuery = doctorsQuery
                    .Where(d => d.ClinicId == clinicId.Value);
            }

            var doctors = await doctorsQuery.ToListAsync();

            // Filter by doctor name search
            if (!string.IsNullOrWhiteSpace(search))
            {
                doctors = doctors
                    .Where(d => d.FullName.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            ViewBag.Search = search;

            var result = new List<DoctorSlotsVM>();

            foreach (var doctor in doctors)
            {
                if (doctor.Clinic == null)
                    continue;

                var slots = new List<DateTime>();

                var cursor = targetDate.Add(doctor.Clinic.StartTime);

                var end = targetDate.Add(doctor.Clinic.EndTime);

                while (cursor.AddMinutes(SlotMinutes) <= end)
                {
                    // Skip past slots when viewing today
                    if (targetDate.Date == DateTime.Today &&
                        cursor < DateTime.Now)
                    {
                        cursor = cursor.AddMinutes(SlotMinutes);
                        continue;
                    }

                    bool taken = bookedToday.Any(a =>
                        a.DoctorId == doctor.DoctorId &&
                        cursor < a.AppointmentDate.AddMinutes(SlotMinutes) &&
                        cursor.AddMinutes(SlotMinutes) > a.AppointmentDate);

                    if (!taken)
                    {
                        slots.Add(cursor);
                    }

                    cursor = cursor.AddMinutes(SlotMinutes);
                }

                if (slots.Any())
                {
                    result.Add(new DoctorSlotsVM
                    {
                        Doctor = doctor,
                        AvailableSlots = slots
                    });
                }
            }

            return View(result);
        }


        public async Task<IActionResult> Index(string search, string status, DateTime? date, string sortOrder, int pageNumber = 1)
        {
            var query = _context.Appointments
                .Include(a => a.Doctor)
                .Include(a => a.Patient)
                .Include(a => a.Clinic)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();

                query = query.Where(a =>
                    (a.Patient != null && EF.Functions.Like(a.Patient.FullName.ToLower(), $"%{search}%")) ||
                    (a.Doctor != null && EF.Functions.Like(a.Doctor.FullName.ToLower(), $"%{search}%")) ||
                    (a.Clinic != null && EF.Functions.Like(a.Clinic.Name.ToLower(), $"%{search}%"))
                );
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(a => a.Status == status);
            }

            switch (sortOrder)
            {
                case "date_asc":
                    query = query.OrderBy(a => a.AppointmentDate);
                    break;

                case "date_desc":
                    query = query.OrderByDescending(a => a.AppointmentDate);
                    break;

                case "doctor":
                    query = query.OrderBy(a => a.Doctor != null ? a.Doctor.FullName : "");
                    break;

                case "patient":
                    query = query.OrderBy(a => a.Patient != null ? a.Patient.FullName : "");
                    break;

                case "clinic":
                    query = query.OrderBy(a => a.Clinic != null ? a.Clinic.Name : "");
                    break;

                case "status":
                    query = query.OrderBy(a => a.Status);
                    break;

                default:
                    query = query.OrderByDescending(a => a.AppointmentId);
                    break;
            }

            int pageSize = 5;
            await AutoCompleteAppointments();
            return View(await PaginatedList<Appointment>.CreateAsync(
                query,
                pageNumber,
                pageSize
            ));
           
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var appointment = await _context.Appointments
                .Include(a => a.Doctor)
                .ThenInclude(d => d.Specialty)
                .Include(a => a.Patient)
                .Include(a => a.Clinic)
                .Include(a => a.History)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppointmentId == id);

            if (appointment == null)
                return NotFound();

            return View(appointment);
        }

        public IActionResult Create(DateTime? dateTime, int? clinicId, int? patientId, int? doctorId, bool returnToHistory = false)
        {
            var appointment = new Appointment();

            if (dateTime.HasValue)
                appointment.AppointmentDate = dateTime.Value;

            if (clinicId.HasValue)
                appointment.ClinicId = clinicId.Value;

            if (patientId.HasValue)
                appointment.PatientId = patientId.Value;

            ViewBag.ReturnToHistory = returnToHistory;
            ViewBag.ReturnPatientId = patientId;

            LoadDropDowns(appointment);

            return View(appointment);
        }

        [HttpGet]
        public async Task<IActionResult> GetDoctorClinic(int doctorId)
        {
            var doctor = await _context.Doctors
                .Include(d => d.Clinic)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DoctorId == doctorId);

            if (doctor == null || doctor.Clinic == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Doctor clinic not found"
                });
            }

            return Json(new
            {
                success = true,
                clinicId = doctor.Clinic.ClinicId,
                clinicName = doctor.Clinic.Name
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Appointment appointment, bool returnToHistory = false, int? returnPatientId = null)
        {
            ModelState.Remove("Doctor");
            ModelState.Remove("Patient");
            ModelState.Remove("Clinic");
            ModelState.Remove("History");

            var doctor = await _context.Doctors
                .Include(d => d.Clinic)
                .FirstOrDefaultAsync(d => d.DoctorId == appointment.DoctorId);

            if (doctor == null || doctor.Clinic == null)
            {
                ModelState.AddModelError("DoctorId", "Please select a valid doctor.");
                LoadDropDowns(appointment);
                return View(appointment);
            }

            appointment.ClinicId = doctor.ClinicId;

            var clinic = doctor.Clinic;

            var appointmentTime = appointment.AppointmentDate.TimeOfDay;

            bool isValidTime;

            if (clinic.StartTime < clinic.EndTime)
            {
                isValidTime =
                    appointmentTime >= clinic.StartTime &&
                    appointmentTime <= clinic.EndTime;
            }
            else
            {
                isValidTime =
                    appointmentTime >= clinic.StartTime ||
                    appointmentTime <= clinic.EndTime;
            }

            if (!isValidTime)
            {
                ModelState.AddModelError("", "Appointment outside clinic working hours");
                LoadDropDowns(appointment);
                return View(appointment);
            }
            if (appointment.AppointmentDate < DateTime.Now)
            {
                ModelState.AddModelError("AppointmentDate","Cannot book an appointment in the past.");
                LoadDropDowns(appointment);
                return View(appointment);
            }


            const int AppointmentDurationMinutes = 30;

            var newStart = appointment.AppointmentDate;
            var newEnd = newStart.AddMinutes(AppointmentDurationMinutes);

            var conflictingAppointment = await _context.Appointments
                .FirstOrDefaultAsync(a =>
                    (
                        a.ClinicId == appointment.ClinicId ||
                        a.DoctorId == appointment.DoctorId ||
                        a.PatientId == appointment.PatientId
                    )
                    &&
                    (
                        newStart < a.AppointmentDate.AddMinutes(AppointmentDurationMinutes) &&
                        newEnd > a.AppointmentDate
                    )
                );

            if (conflictingAppointment != null)
            {
                if (conflictingAppointment.DoctorId == appointment.DoctorId)
                {
                    ModelState.AddModelError("",
                        $"Doctor is already booked from {conflictingAppointment.AppointmentDate:HH:mm} " +
                        $"to {conflictingAppointment.AppointmentDate.AddMinutes(AppointmentDurationMinutes):HH:mm}");
                }

                if (conflictingAppointment.PatientId == appointment.PatientId)
                {
                    ModelState.AddModelError("",
                        $"Patient already has an appointment from {conflictingAppointment.AppointmentDate:HH:mm} " +
                        $"to {conflictingAppointment.AppointmentDate.AddMinutes(AppointmentDurationMinutes):HH:mm}");
                }

                if (conflictingAppointment.ClinicId == appointment.ClinicId)
                {
                    ModelState.AddModelError("",
                        $"Clinic is occupied from {conflictingAppointment.AppointmentDate:HH:mm} " +
                        $"to {conflictingAppointment.AppointmentDate.AddMinutes(AppointmentDurationMinutes):HH:mm}");
                }

                LoadDropDowns(appointment);
                return View(appointment);
            }

            var patientsCount = await _context.Appointments
                .CountAsync(a =>
                    a.ClinicId == appointment.ClinicId &&
                    a.AppointmentDate.Date == appointment.AppointmentDate.Date
                );

            if (patientsCount >= clinic.MaxPatients)
            {
                ModelState.AddModelError("", "Clinic reached maximum patients today");
                LoadDropDowns(appointment);
                return View(appointment);
            }

            if (!ModelState.IsValid)
            {
                LoadDropDowns(appointment);
                return View(appointment);
            }

            _context.Add(appointment);

            await _context.SaveChangesAsync();
            if (returnToHistory)
            {
                return RedirectToAction(
                    "Create",
                    "Histories",
                    new { patientId = appointment.PatientId }
                );
            }
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var appointment = await _context.Appointments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppointmentId == id);

            if (appointment == null)
                return NotFound();

            if (appointment.AppointmentDate < DateTime.Now)
            {
                ModelState.AddModelError("AppointmentDate","Cannot book an appointment in the past.");
                LoadDropDowns(appointment);
                return View(appointment);
            }

            LoadDropDowns(appointment);

            return View(appointment);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Appointment appointment)
        {
            if (id != appointment.AppointmentId)
                return NotFound();

            ModelState.Remove("Doctor");
            ModelState.Remove("Patient");
            ModelState.Remove("Clinic");
            ModelState.Remove("History");

            var doctor = await _context.Doctors
                .Include(d => d.Clinic)
                .FirstOrDefaultAsync(d => d.DoctorId == appointment.DoctorId);

            if (doctor == null || doctor.Clinic == null)
            {
                ModelState.AddModelError("DoctorId", "Please select a valid doctor.");
                LoadDropDowns(appointment);
                return View(appointment);
            }

            appointment.ClinicId = doctor.ClinicId;

            var clinic = doctor.Clinic;

            const int AppointmentDurationMinutes = 30;

            var appointmentTime = appointment.AppointmentDate.TimeOfDay;

            bool isValidTime =
                clinic.StartTime < clinic.EndTime
                    ? appointmentTime >= clinic.StartTime && appointmentTime <= clinic.EndTime
                    : appointmentTime >= clinic.StartTime || appointmentTime <= clinic.EndTime;

            if (!isValidTime)
            {
                ModelState.AddModelError("", "Appointment outside clinic working hours");
                LoadDropDowns(appointment);
                return View(appointment);
            }

            var newStart = appointment.AppointmentDate;
            var newEnd = newStart.AddMinutes(AppointmentDurationMinutes);

            var conflict = await _context.Appointments.FirstOrDefaultAsync(a =>
                a.AppointmentId != appointment.AppointmentId &&
                (
                    a.DoctorId == appointment.DoctorId ||
                    a.PatientId == appointment.PatientId ||
                    a.ClinicId == appointment.ClinicId
                ) &&
                (
                    newStart < a.AppointmentDate.AddMinutes(AppointmentDurationMinutes) &&
                    newEnd > a.AppointmentDate
                )
            );

            if (conflict != null)
            {
                if (conflict.DoctorId == appointment.DoctorId)
                    ModelState.AddModelError("", "Doctor already has an overlapping appointment.");

                if (conflict.PatientId == appointment.PatientId)
                    ModelState.AddModelError("", "Patient already has an overlapping appointment.");

                if (conflict.ClinicId == appointment.ClinicId)
                    ModelState.AddModelError("", "Clinic already has an overlapping appointment.");

                LoadDropDowns(appointment);
                return View(appointment);
            }

            if (!ModelState.IsValid)
            {
                LoadDropDowns(appointment);
                return View(appointment);
            }

            try
            {
                _context.Update(appointment);

                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AppointmentExists(appointment.AppointmentId))
                    return NotFound();
                else
                    throw;
            }

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var appointment = await _context.Appointments
                .Include(a => a.Doctor)
                .Include(a => a.Patient)
                .Include(a => a.Clinic)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppointmentId == id);

            if (appointment == null)
                return NotFound();

            return View(appointment);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var appointment = await _context.Appointments
                .FirstOrDefaultAsync(a => a.AppointmentId == id);

            if (appointment == null)
                return NotFound();

            var history = await _context.Histories
                .FirstOrDefaultAsync(h =>
                    h.AppointmentId.HasValue &&
                    h.AppointmentId.Value == id);

            if (history != null)
            {
                _context.Histories.Remove(history);
            }

            _context.Appointments.Remove(appointment);

            await _context.SaveChangesAsync();

            TempData["Success"] =
                "Appointment and related history deleted successfully";

            return RedirectToAction(nameof(Index));
        }

        private void LoadDropDowns(Appointment appointment = null)
        {
            ViewData["DoctorId"] =
                new SelectList(_context.Doctors, "DoctorId", "FullName", appointment?.DoctorId);

            ViewData["PatientId"] =
                new SelectList(_context.Patients, "PatientId", "FullName", appointment?.PatientId);

            ViewData["ClinicId"] =
                new SelectList(_context.Clinics, "ClinicId", "Name", appointment?.ClinicId);
        }

        private bool AppointmentExists(int id)
        {
            return _context.Appointments.Any(e => e.AppointmentId == id);
        }

        //completed appointments
        private async Task AutoCompleteAppointments()
        {
            var now = DateTime.Now;

            var expiredAppointments = await _context.Appointments
                .Where(a => a.Status != "Completed"
                         && a.AppointmentDate.AddMinutes(30) < now)
                .ToListAsync();

            foreach (var app in expiredAppointments)
            {
                app.Status = "Completed";
            }

            if (expiredAppointments.Any())
                await _context.SaveChangesAsync();
        }
    }
}