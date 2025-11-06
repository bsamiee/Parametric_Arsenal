using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arsenal.Core.Errors;

namespace Arsenal.Core.Results;

/// <summary>Monadic result container with lazy evaluation and functional composition.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct Result<T> : IEquatable<Result<T>> {
    private readonly T _value;
    private readonly SystemError[] _errors;
    private readonly bool _isSuccess;
    private readonly Func<Result<T>>? _deferred;

    [Pure] public bool IsDeferred => this._deferred is not null;
    [Pure] public bool IsSuccess => this.Eval._isSuccess;
    [Pure] public T Value => this.Eval._isSuccess ? this.Eval._value : Throw<T>();
    [Pure] public IReadOnlyList<SystemError> Errors => this.Eval._isSuccess ? [] : this.Eval._errors;
    [Pure] public SystemError Error => this.Eval switch { { _errors: [var head, ..] } => head, _ => default };
    [Pure] private Result<T> Eval => this._deferred is not null ? this._deferred().Eval : this;

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

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static TReturn Throw<TReturn>() => throw new InvalidOperationException(ResultErrors.State.InvalidAccess.Message);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Result<T> other) {
        (Result<T> selfEval, Result<T> otherEval) = (this.Eval, other.Eval);
        return (selfEval._isSuccess, otherEval._isSuccess) switch {
            (true, true) => EqualityComparer<T>.Default.Equals(selfEval._value, otherEval._value),
            (false, false) => (selfEval._errors ?? []).SequenceEqual(otherEval._errors ?? []),
            _ => false,
        };
    }

    /// <summary>Attempts to extract value with safe null handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet([MaybeNullWhen(false)] out T value) =>
        this.IsSuccess switch {
            true => (value = this.Value, true).Item2,
            false => (value = default, false).Item2,
        };

    /// <summary>Transforms success values using monadic functor semantics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TOut> Map<TOut>(Func<T, TOut> transform) {
        ArgumentNullException.ThrowIfNull(transform);
        Result<T> self = this;
        return self.IsDeferred switch {
            true => ResultFactory.Create(deferred: () => self.Eval.Map(transform)),
            false => self.Eval switch { { _isSuccess: true, _value: var value } => ResultFactory.Create(value: transform(value)), { _errors: var errs } => ResultFactory.Create<TOut>(errors: errs ?? []),
            },
        };
    }

    /// <summary>Chains monadic operations with flatMap semantics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> operation) {
        ArgumentNullException.ThrowIfNull(operation);
        Result<T> self = this;
        return self.IsDeferred switch {
            true => ResultFactory.Create(deferred: () => self.Eval.Bind(operation)),
            false => self.Eval switch { { _isSuccess: true, _value: var value } => operation(value), { _errors: var errs } => ResultFactory.Create<TOut>(errors: errs ?? []),
            },
        };
    }

    /// <summary>Filters values using validation predicate with error specification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Filter(Func<T, bool> predicate, SystemError error) {
        ArgumentNullException.ThrowIfNull(predicate);
        Result<T> self = this;
        return self.IsDeferred switch {
            true => ResultFactory.Create(deferred: () => self.Eval.Filter(predicate, error)),
            false => self.Eval switch { { _isSuccess: true, _value: var value } when !predicate(value) => ResultFactory.Create<T>(errors: [error]),
                _ => self,
            },
        };
    }

    /// <summary>Pattern matches success and failure cases for result transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<SystemError[], TResult> onFailure) {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return this.Eval switch { { _isSuccess: true, _value: var value } => onSuccess(value), { _errors: var errs } => onFailure(errs ?? []),
        };
    }

    /// <summary>Executes side effects without changing Result state (monadic tap).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Tap(Action<T>? onSuccess = null, Action<SystemError[]>? onFailure = null) {
        Result<T> self = this;
        return self.Match(
            onSuccess: value => { onSuccess?.Invoke(value); return self; },
            onFailure: errors => { onFailure?.Invoke(errors); return self; });
    }

    /// <summary>Applicative Apply for parallel validation with error accumulation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TOut> Apply<TOut>(Result<Func<T, TOut>> func) {
        Result<T> self = this;
        Result<Func<T, TOut>> f = func;
        return (self.IsDeferred, f.IsDeferred) switch {
            (true, _) or (_, true) => ResultFactory.Create(deferred: () => self.Eval.Apply(f.Eval)),
            _ => (self.Eval, f.Eval) switch {
                ( { _isSuccess: true, _value: var value }, { _isSuccess: true, _value: var invoker }) =>
                    ResultFactory.Create(value: invoker(value)),
                (var left, var right) => ResultFactory.Create<TOut>(errors: [
                    .. right._errors ?? [],
                    .. left._errors ?? [],
                ]),
            },
        };
    }

    /// <summary>Transforms errors without changing success state.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> MapError(Func<SystemError[], SystemError[]> transform) {
        ArgumentNullException.ThrowIfNull(transform);
        Result<T> self = this;
        return self.IsDeferred switch {
            true => ResultFactory.Create(deferred: () => self.Eval.MapError(transform)),
            false => self.Eval switch {
                { _isSuccess: true } => self,
                { _errors: var errs } => ResultFactory.Create<T>(errors: transform(errs ?? [])),
            },
        };
    }

    /// <summary>Recovers from errors by producing a value.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Recover(Func<SystemError[], T> recovery) {
        ArgumentNullException.ThrowIfNull(recovery);
        Result<T> self = this;
        return self.IsDeferred switch {
            true => ResultFactory.Create(deferred: () => self.Eval.Recover(recovery)),
            false => self.Eval switch {
                { _isSuccess: true } => self,
                { _errors: var errs } => ResultFactory.Create(value: recovery(errs ?? [])),
            },
        };
    }

    /// <summary>Recovers from errors by producing a new Result.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> RecoverWith(Func<SystemError[], Result<T>> recovery) {
        ArgumentNullException.ThrowIfNull(recovery);
        Result<T> self = this;
        return self.IsDeferred switch {
            true => ResultFactory.Create(deferred: () => self.Eval.RecoverWith(recovery)),
            false => self.Eval switch {
                { _isSuccess: true } => self,
                { _errors: var errs } => recovery(errs ?? []),
            },
        };
    }

    /// <summary>Ensures single predicate holds or returns error.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Ensure(Func<T, bool> predicate, SystemError error) {
        ArgumentNullException.ThrowIfNull(predicate);
        Result<T> self = this;
        return self.IsDeferred switch {
            true => ResultFactory.Create(deferred: () => self.Eval.Ensure(predicate, error)),
            false => self.Eval switch {
                { _isSuccess: false } => self,
                { _value: var value } when !predicate(value) => ResultFactory.Create<T>(errors: [error]),
                _ => self,
            },
        };
    }

    /// <summary>Ensures all predicates hold or accumulates validation errors.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> EnsureAll(params (Func<T, bool> Predicate, SystemError Error)[] validations) {
        ArgumentNullException.ThrowIfNull(validations);
        Result<T> self = this;
        return self.IsDeferred switch {
            true => ResultFactory.Create(deferred: () => self.Eval.EnsureAll(validations)),
            false => self.Eval switch {
                { _isSuccess: false } => self,
                { _value: var value } => validations
                    .Where(v => !v.Predicate(value))
                    .Select(v => v.Error)
                    .ToArray() switch {
                        { Length: > 0 } errors => ResultFactory.Create<T>(errors: errors),
                        _ => self,
                    },
            },
        };
    }

    /// <summary>Transforms collection elements to Results with error accumulation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<IReadOnlyList<TOut>> Traverse<TOut>(Func<T, Result<TOut>> selector) {
        ArgumentNullException.ThrowIfNull(selector);
        Result<T> self = this;
        return self.IsDeferred switch {
            true => ResultFactory.Create(deferred: () => self.Eval.Traverse(selector)),
            false => self.Eval switch { { _isSuccess: true, _value: System.Collections.IEnumerable collection } when collection is not string =>
                                            collection.Cast<object>().Aggregate(
                                                ResultFactory.Create<IReadOnlyList<TOut>>(value: new List<TOut>().AsReadOnly()),
                                                (acc, item) => acc.Accumulate(selector((T)item))), { _isSuccess: true, _value: var value } => selector(value).Map(val => (IReadOnlyList<TOut>)[val]), { _errors: var errs } => ResultFactory.Create<IReadOnlyList<TOut>>(errors: errs ?? []),
            },
        };
    }

    /// <summary>SelectMany for LINQ query syntax support (alias for Bind).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TOut> SelectMany<TOut>(Func<T, Result<TOut>> selector) => this.Bind(selector);

    /// <summary>SelectMany with projection for LINQ query syntax support.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TOut> SelectMany<TIntermediate, TOut>(
        Func<T, Result<TIntermediate>> selector,
        Func<T, TIntermediate, TOut> projection) =>
        this.Bind(outer => selector(outer).Map(inner => projection(outer, inner)));
}
