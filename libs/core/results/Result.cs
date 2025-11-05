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

    /// <summary>Executes side effects without changing Result state.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Apply(Action<T>? onSuccess = null, Action<SystemError[]>? onFailure = null) {
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

    /// <summary>Reduces Result to accumulated value with optional error handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAcc Reduce<TAcc>(TAcc seed, Func<TAcc, T, TAcc> onSuccess, Func<TAcc, SystemError[], TAcc>? onFailure = null) {
        ArgumentNullException.ThrowIfNull(onSuccess);
        Result<T> eval = this.Eval;
        return (eval, onFailure) switch {
            ( { _isSuccess: true, _value: var value }, _) => onSuccess(seed, value),
            ( { _errors: var errs }, var handler) when handler is not null => handler(seed, errs ?? []),
            _ => seed,
        };
    }

    /// <summary>Handles errors with transformation and recovery strategies.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> OnError(Func<SystemError[], SystemError[]>? mapError = null, Func<SystemError[], T>? recover = null, Func<SystemError[], Result<T>>? recoverWith = null) {
        Result<T> self = this;
        return self.IsDeferred switch {
            true => ResultFactory.Create(deferred: () => self.Eval.OnError(mapError, recover, recoverWith)),
            false => self.Eval switch { { _isSuccess: true } => self,
                var failure => (failure._errors ?? []) switch {
                    var errs when mapError is not null => ResultFactory.Create<T>(errors: mapError(errs)),
                    var errs when recover is not null => ResultFactory.Create(value: recover(errs)),
                    var errs when recoverWith is not null => recoverWith(errs),
                    _ => self,
                },
            },
        };
    }

    /// <summary>Ensures predicate holds or accumulates validation errors.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Ensure(params object[] validations) {
        Result<T> self = this;
        return (self.IsDeferred, self.Eval) switch {
            (true, _) => ResultFactory.Create(deferred: () => self.Eval.Ensure(validations)),
            (_, { _isSuccess: false }) => self,
            (_, { _value: var value }) => validations switch {
                [Func<T, bool> predicate, SystemError error] => predicate(value) ? self : ResultFactory.Create<T>(errors: [error]),
                [(Func<T, bool>, SystemError)[] grouped] => grouped.Where(v => !v.Item1(value)).Select(v => v.Item2).ToArray() switch { { Length: > 0 } errors => ResultFactory.Create<T>(errors: errors),
                    _ => self,
                },
                _ when validations.All(v => v is (Func<T, bool>, SystemError)) =>
                    validations.Cast<(Func<T, bool>, SystemError)>().Where(v => !v.Item1(value)).Select(v => v.Item2).ToArray() switch { { Length: > 0 } errors => ResultFactory.Create<T>(errors: errors),
                        _ => self,
                    },
                [] => self,
                _ => throw new ArgumentException(ResultErrors.Factory.InvalidValidateParameters.Message, nameof(validations)),
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
                                                (acc, item) => acc.Apply(selector((T)item).Map<Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>>(
                                                    val => list => [.. list, val]))), { _isSuccess: true, _value: var value } => selector(value).Map(val => (IReadOnlyList<TOut>)[val]), { _errors: var errs } => ResultFactory.Create<IReadOnlyList<TOut>>(errors: errs ?? []),
            },
        };
    }
}
