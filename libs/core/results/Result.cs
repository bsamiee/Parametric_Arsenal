using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arsenal.Core.Errors;

namespace Arsenal.Core.Results;

/// <summary>Monadic result with lazy evaluation and functional composition.</summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{DebuggerDisplay}")]
public readonly struct Result<T> : IEquatable<Result<T>> {
    private readonly T _value;
    private readonly SystemError[] _errors;
    private readonly bool _isSuccess;
    private readonly Func<Result<T>>? _deferred;

    [Pure] public bool IsDeferred => this._deferred is not null;
    [Pure] public bool IsSuccess => this.Eval._isSuccess;
    [Pure] public T Value => this.Eval._isSuccess ? this.Eval._value : default!;
    [Pure] public IReadOnlyList<SystemError> Errors => this.Eval._isSuccess ? [] : this.Eval._errors;
    [Pure] public SystemError Error => this.Eval switch { { _errors: [SystemError head, ..] } => head, _ => default };
    [Pure] private Result<T> Eval => this._deferred is not null ? this._deferred().Eval : this;

    [Pure] private string DebuggerDisplay => (this._deferred is not null, this._isSuccess, this._errors) switch {
        (true, _, _) => string.Create(CultureInfo.InvariantCulture, $"Deferred<{typeof(T).Name}>"),
        (false, true, _) => string.Create(CultureInfo.InvariantCulture, $"Success: {this._value?.ToString() ?? "null"}"),
        (false, false, [SystemError single]) => string.Create(CultureInfo.InvariantCulture, $"Error: {single}"),
        (false, false, { Length: > 0 } errors) => string.Create(CultureInfo.InvariantCulture, $"Errors({errors.Length}): {errors[0]}"),
        (false, false, _) => "Errors(0): none",
    };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Result(bool isSuccess, T value, SystemError[] errors, Func<Result<T>>? deferred) {
        this._isSuccess = isSuccess;
        this._value = value;
        this._errors = errors;
        this._deferred = deferred;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Result<T> o && this.Equals(o);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() {
        Result<T> eval = this.Eval;
        return eval._isSuccess switch {
            true => (true, eval._value).GetHashCode(),
            false => (false, eval._errors).GetHashCode(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Result<T> other) {
        (Result<T> selfEval, Result<T> otherEval) = (this.Eval, other.Eval);
        return (selfEval._isSuccess, otherEval._isSuccess) switch {
            (true, true) => EqualityComparer<T>.Default.Equals(selfEval._value, otherEval._value),
            (false, false) => (selfEval._errors ?? []).SequenceEqual(otherEval._errors ?? []),
            _ => false,
        };
    }

    /// <summary>Maps success value through transform.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TOut> Map<TOut>(Func<T, TOut> transform) {
        Result<T> self = this;
        return transform is null
            ? ResultFactory.Create<TOut>(error: E.Results.NoValueProvided.WithContext("transform"))
            : self.IsDeferred
                ? ResultFactory.Create(deferred: () => self.Eval.Map(transform))
                : self.Eval switch { { _isSuccess: true, _value: T value } => ResultFactory.Create(value: transform(value)), { _errors: SystemError[] errs } => ResultFactory.Create<TOut>(errors: errs ?? []),
                };
    }

    /// <summary>Chains operation returning Result.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> operation) {
        Result<T> self = this;
        return operation is null
            ? ResultFactory.Create<TOut>(error: E.Results.NoValueProvided.WithContext("operation"))
            : self.IsDeferred
                ? ResultFactory.Create(deferred: () => self.Eval.Bind(operation))
                : self.Eval switch { { _isSuccess: true, _value: T value } => operation(value), { _errors: SystemError[] errs } => ResultFactory.Create<TOut>(errors: errs ?? []),
                };
    }

    /// <summary>Validates value using predicate.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Ensure(Func<T, bool> predicate, SystemError error) {
        Result<T> self = this;
        return predicate is null
            ? ResultFactory.Create<T>(error: E.Results.NoValueProvided.WithContext("predicate"))
            : self.IsDeferred
                ? ResultFactory.Create(deferred: () => self.Eval.Ensure(predicate, error))
                : self.Eval switch { { _isSuccess: true, _value: T value } when !predicate(value) => ResultFactory.Create<T>(errors: [error]),
                    _ => self,
                };
    }

    /// <summary>Validates value using multiple predicates.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Ensure(params (Func<T, bool>, SystemError)[] validations) {
        Result<T> self = this;
        return (self.IsDeferred, self.Eval) switch {
            (true, _) => ResultFactory.Create(deferred: () => self.Eval.Ensure(validations)),
            (_, { _isSuccess: false }) => self,
            (_, { _value: T value }) => validations.Any(v => !v.Item1(value))
                ? ResultFactory.Create<T>(errors: [.. validations
                    .Where(v => !v.Item1(value))
                    .Select(v => v.Item2),
                ])
                : self,
            _ => self,
        };
    }

    /// <summary>Matches success/failure and returns result.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<SystemError[], TResult> onFailure) {
        Func<T, TResult> success = onSuccess ?? (_ => default!);
        Func<SystemError[], TResult> failure = onFailure ?? (_ => default!);
        return this.Eval switch { { _isSuccess: true, _value: T value } => success(value), { _errors: SystemError[] errs } => failure(errs ?? []),
        };
    }

    /// <summary>Executes side effects and returns unchanged Result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Tap(Action<T>? onSuccess = null, Action<SystemError[]>? onFailure = null) {
        Result<T> self = this;
        return self.Match(
            onSuccess: value => { onSuccess?.Invoke(value); return self; },
            onFailure: errors => { onFailure?.Invoke(errors); return self; });
    }

    /// <summary>Applies function in Result to value in Result.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TOut> Apply<TOut>(Result<Func<T, TOut>> func) {
        Result<T> self = this;
        Result<Func<T, TOut>> f = func;
        return (self.IsDeferred, f.IsDeferred) switch {
            (true, _) or (_, true) => ResultFactory.Create(deferred: () => self.Eval.Apply(f.Eval)),
            _ => (self.Eval, f.Eval) switch {
                ( { _isSuccess: true, _value: T value }, { _isSuccess: true, _value: Func<T, TOut> invoker }) =>
                    ResultFactory.Create(value: invoker(value)),
                (Result<T> left, Result<Func<T, TOut>> right) => ResultFactory.Create<TOut>(errors: [
                    .. right._errors ?? [],
                    .. left._errors ?? [],
                ]),
            },
        };
    }

    /// <summary>Maps errors on failure.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> OnError(Func<SystemError[], SystemError[]> mapError) {
        Result<T> self = this;
        return mapError is null
            ? ResultFactory.Create<T>(error: E.Results.NoValueProvided.WithContext("mapError"))
            : self.IsDeferred
                ? ResultFactory.Create(deferred: () => self.Eval.OnError(mapError))
                : self.Eval switch { { _isSuccess: true } => self,
                    Result<T> failure => ResultFactory.Create<T>(errors: mapError(failure._errors ?? [])),
                };
    }

    /// <summary>Recovers from failure with value.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> OnError(Func<SystemError[], T> recover) {
        Result<T> self = this;
        return recover is null
            ? ResultFactory.Create<T>(error: E.Results.NoValueProvided.WithContext("recover"))
            : self.IsDeferred
                ? ResultFactory.Create(deferred: () => self.Eval.OnError(recover))
                : self.Eval switch { { _isSuccess: true } => self,
                    Result<T> failure => ResultFactory.Create(value: recover(failure._errors ?? [])),
                };
    }

    /// <summary>Recovers from failure with alternative Result.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> OnError(Func<SystemError[], Result<T>> recoverWith) {
        Result<T> self = this;
        return recoverWith is null
            ? ResultFactory.Create<T>(error: E.Results.NoValueProvided.WithContext("recoverWith"))
            : self.IsDeferred
                ? ResultFactory.Create(deferred: () => self.Eval.OnError(recoverWith))
                : self.Eval switch { { _isSuccess: true } => self,
                    Result<T> failure => recoverWith(failure._errors ?? []),
                };
    }

    /// <summary>Maps collection elements through Result function.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<IReadOnlyList<TOut>> Traverse<TOut>(Func<T, Result<TOut>> selector) {
        Result<T> self = this;
        return selector is null
            ? ResultFactory.Create<IReadOnlyList<TOut>>(error: E.Results.NoValueProvided.WithContext("selector"))
            : self.IsDeferred
                ? ResultFactory.Create(deferred: () => self.Eval.Traverse(selector))
                : self.Eval switch { { _isSuccess: true, _value: System.Collections.IEnumerable collection } when collection is not string => collection.Cast<object>().Aggregate(ResultFactory.Create<IReadOnlyList<TOut>>(value: []), (acc, item) => acc.Accumulate(selector((T)item))), { _isSuccess: true, _value: T value } => selector(value).Map(val => (IReadOnlyList<TOut>)[val]), { _errors: SystemError[] errs } => ResultFactory.Create<IReadOnlyList<TOut>>(errors: errs ?? []),
                };
    }
}
