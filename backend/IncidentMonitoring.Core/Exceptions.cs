namespace IncidentMonitoring.Core;

/// <summary>Mapped to HTTP 404 by the API.</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>Mapped to HTTP 409 by the API.</summary>
public class InvalidStatusTransitionException(string message) : Exception(message);
