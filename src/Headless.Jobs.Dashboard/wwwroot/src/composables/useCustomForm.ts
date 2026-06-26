import { reactive, type UnwrapRef } from 'vue';
import * as yup from 'yup';
import { type Path, type PathWithSuffix, type PathValue } from '@/utilities/pathTypes';

export interface ExtendedFormOptions<TData extends Record<string, unknown>> {
  initialValues: TData;
  validationSchema?:
  | yup.ObjectSchema<TData>
  | ((validator: typeof yup) => Partial<Record<keyof TData, yup.Schema<TData[keyof TData]>>>);
  onResetForm?: () => void;
  onSubmitForm?: (values: UnwrapRef<TData>, errors?: Record<string, string>) => void;
  onFieldUpdate?: {
    [P in PathWithSuffix<TData>]?: (value: PathValue<TData, P>, update: (newValue: PathValue<TData, P>) => void) => void;
  };
}

/* -------------------- Utility Functions -------------------- */

function getAllPaths(obj: unknown, prefix = ''): string[] {
  return Object.entries(obj as Record<string, unknown>).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    return value && typeof value === 'object' && !Array.isArray(value)
      ? getAllPaths(value, path)
      : [path];
  });
}

function getValue(obj: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>((acc, key) => (acc as Record<string, unknown> | undefined)?.[key], obj);
}

function setValue(obj: unknown, path: string, newValue: unknown): void {
  const keys = path.split('.');
  const lastKey = keys.pop();
  if (!lastKey) return;
  const target = keys.reduce<Record<string, unknown>>(
    (acc, key) => (acc[key] ??= {}) as Record<string, unknown>,
    obj as Record<string, unknown>,
  );
  target[lastKey] = newValue;
}

function deepClone<T>(obj: T): T {
  return JSON.parse(JSON.stringify(obj));
}

/* -------------------- Core Form Implementation -------------------- */

interface CustomForm<TData extends Record<string, unknown>> {
  values: UnwrapRef<TData>;
  errors: Record<string, string>;
  validate: () => Promise<void>;
  validateField: (path: string) => Promise<void>;
}

function useCustomForm<TData extends Record<string, unknown>>(options: ExtendedFormOptions<TData>): CustomForm<TData> {
  const values = reactive(options.initialValues) as UnwrapRef<TData>;
  const errors = reactive<Record<string, string>>({});

  const schema = options.validationSchema
    ? typeof options.validationSchema === 'function'
      ? yup.object(
        Object.fromEntries(
          Object.entries(options.validationSchema(yup) || {}).filter(
            ([, value]) => value !== undefined
          )
        ) as yup.ObjectShape
      )
      : options.validationSchema
    : undefined;

  async function validate() {
    Object.keys(errors).forEach(path => delete errors[path]);
    
    if (schema) {
      try {
        await schema.validate(values, { abortEarly: false });
      } catch (e) {
        if (e instanceof yup.ValidationError) {
          e.inner.forEach((err) => {
            if (err.path) errors[err.path] = err.message;
          });
        }
      }
    }
  }

  async function validateField(path: string): Promise<void> {
    delete errors[path];
    if (schema) {
      const schemaPaths = Object.keys(schema.describe().fields); // Get all defined schema fields

      if (!schemaPaths.includes(path)) {
        return;
      }

      try {
        await schema.validateAt(path, values);
      } catch (err) {
        errors[path] = err instanceof Error ? err.message : String(err);
      }
    }
  }


  return { values, errors, validate, validateField };
}

/* -------------------- Exposed Composable -------------------- */

export function useForm<TData extends Record<string, unknown>>(options: ExtendedFormOptions<TData>) {
  const initialValuesClone = deepClone(options.initialValues);
  const { values, errors, validate, validateField } = useCustomForm(options);
  const allPaths: Path<TData>[] = getAllPaths(options.initialValues) as Path<TData>[];
  const touched = reactive<Record<string, boolean>>(Object.fromEntries(allPaths.map(path => [path, false])));


  function setFieldValue<P extends Path<TData>>(path: P, value: PathValue<TData, P>) {
    const updateFn = options.onFieldUpdate?.[path] as
          | ((value: PathValue<TData, P>, update: (newValue: PathValue<TData, P>) => void) => PathValue<TData, P>)
          | undefined;

    if (updateFn) {
      updateFn(value, (updatedValue) => setValue(values, path, updatedValue));
    }
    else{
      setValue(values, path, value);
    }
  }

  function resetForm() {
    allPaths.forEach((path: Path<TData>) => {
      setFieldValue(path, getValue(initialValuesClone, path) as PathValue<TData, Path<TData>>);
      touched[path] = false;
    });

    options.onResetForm?.();
  }

  function resetField(path: Path<TData>, forceUpdateInitialValue?: PathValue<TData, Path<TData>>) {
    if (forceUpdateInitialValue) {
      setValue(initialValuesClone, path, forceUpdateInitialValue);
    }

    setFieldValue(path, getValue(initialValuesClone, path) as PathValue<TData, Path<TData>>);

    touched[path] = false;
  }

  async function handleSubmit(e?: Event) {
    e?.preventDefault?.();

    allPaths.forEach(path => (touched[path] = true));

    await validate();

    if (Object.keys(errors).length) {
      options.onSubmitForm?.(values, errors);
    } else {
      options.onSubmitForm?.(values);
    }
  }

  function getFieldValue<P extends Path<TData>>(path: P): PathValue<TData, P> {
    return getValue(values, path) as PathValue<TData, P>;
  }

  function bindField<P extends Path<TData>>(path: P, modelValueTransformer?: (value: string | number | Date) => PathValue<TData, P>) {
    return {
      modelValue: getFieldValue(path),
      'onUpdate:modelValue': async (val: unknown) => {
        const newValue: PathValue<TData, P> = modelValueTransformer
          ? modelValueTransformer(val as string | number | Date)
          : (val as PathValue<TData, P>);

        const updateFn = options.onFieldUpdate?.[path] as
          | ((value: PathValue<TData, P>, update: (newValue: PathValue<TData, P>) => void) => PathValue<TData, P>)
          | undefined;

        if (updateFn) {
          updateFn(val as PathValue<TData, P>, (updatedValue) => setFieldValue(path, updatedValue));
        } else {
          setFieldValue(path, newValue);
        }

        touched[path] = true;
        await validateField(path);
      },
      onBlur: async () => {
        touched[path] = true;
        let finalValue: PathValue<TData, P> = getValue(values, path) as PathValue<TData, P>;

        const blurUpdateFn = options.onFieldUpdate?.[`${path}__blur` as PathWithSuffix<TData>] as
          | ((value: PathValue<TData, P>, update: (newValue: PathValue<TData, P>) => void) => PathValue<TData, P>)
          | undefined;

        if (blurUpdateFn) {
          finalValue = blurUpdateFn(finalValue, (updatedValue) => setFieldValue(path, updatedValue));
        }
        else {
          setFieldValue(path, finalValue);
        }

        await validateField(path);
      },
      errorMessages: touched[path] && errors[path] ? [errors[path]] : [],
    };
  }

  return { values, errors, touched, resetForm, bindField, setFieldValue, handleSubmit, getFieldValue, resetField };
}