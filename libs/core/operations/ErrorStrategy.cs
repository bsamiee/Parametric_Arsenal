namespace Arsenal.Core.Operations;

/// <summary>Error handling strategy for batch operations</summary>
public enum ErrorStrategy {
	/// <summary>Stop on first error and return immediately</summary>
	FailFast,

	/// <summary>Continue processing and accumulate all errors</summary>
	AccumulateAll,

	/// <summary>Skip failed items and continue with valid results</summary>
	SkipFailed,
}
