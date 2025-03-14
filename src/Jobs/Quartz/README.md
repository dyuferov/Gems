# Gems.Jobs.Quartz

Содержит вспомогательные классы для сервисов-планировщиков на основе Quartz

Библиотека предназначена для следующих сред выполнения (и новее):

.NET 6.0

# Содержание

* [Установка](#установка)
* [Перезапуск задачи по ошибке](#перезапуск-задачи-по-ошибке)
* [Регистрация конкурентной задачи](#регистрация-конкурентной-задачи)
* [Получить список активных джобов с триггерами](#получить-список-активных-джобов-с-триггерами)
* [Добавление задачи](#добавление-задачи)
* [Отключение задачи](#отключение-задачи)

# Установка
- Установите nuget пакет Gems.Jobs.Quartz через менеджер пакетов
- Добавьте следующие строки в appsettings
```json
  "Jobs": {
    "SchedulerName": "Service Name",            // наименование планировщика
    "TablePrefix": "quartz.qrtz_",              // префикс таблиц, хранящих данные по элементам Quartz(Jobs, Triggers etc.)
    "JobRecoveryDelayInMilliseconds": 600000,   // Задержка перед итерацией мониторинга и восстановления триггеров, находящихся в состоянии Error (по умолчанию 15000)
    "Triggers": {                               // словарь триггеров
      "UploadSelloutGoods": "0 0 0 * * ?"       // триггер, где ключ - наименование задания, значение - крон выполнения
    },
    "MaxConcurrency": 25,                       // (опционально, по умолчанию 25) Количество потоков, доступных для одновременного выполнения заданий в Quartz    
    "BatchTriggerAcquisitionMaxCount": 1,       // (опционально, по умолчанию 1) Количество тригеров, доступных для получения узлов планировщика за раз
    "AcquireTriggersWithinLock": true,          // (опционально, по умолчанию false) Получение следующих тригеров, происходит с блокировкой бд. Необходимо ставить true, если BatchTriggerAcquisitionMaxCount > 1.
    "QuartzProperties": {                       // установка параметров quartz.*. Данные параметры, переопределяют все раннее установленные параметры (SchedulerName, TablePrefix, MaxConcurrency, BatchTriggerAcquisitionMaxCount, AcquireTriggersWithinLock и другие, установленные при инициализации библиотекой Quartz).
         "ThreadPool": { ... },                 // установка параметров quartz.threadPool.* (см. описание https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html#threadpool)
         "Scheduler": {
             ...                                // установка параметров quartz.scheduler.* (см. описание https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html#main-configuration)
             "SchedulerExporter": { ... },      // установка параметров quartz.threadPool.* (см. описание https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html#remoting-server-and-client)
          },         
         "JobStore": { ... },                   // установка параметров quartz.threadPool.* (см. описание https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html#jobstoretx-ado-net)
         "DataSource": { ... }                  // установка параметров quartz.threadPool.* (см. описание https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html#datasources-ado-net-jobstores)
    }
}
```
- Добавьте регистрацию Quartz в конфигурацию сервисов в классе Startup.cs
```csharp
//...
    public void ConfigureServices(IServiceCollection services)
    {
        //...
        services.AddQuartzWithJobs(this.Configuration, options => options.RegisterJobsFromAssemblyContaining<Startup>());
        //...
    }
    
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        //...
        app.UseQuartzAdminUI(this.Configuration);
        //...
    }
//...
```

- Примените миграцию для БД
С использованием sql-скриптов
```sql
DO
$do$
    BEGIN
        IF NOT EXISTS(
            SELECT
            FROM pg_catalog.pg_roles
            WHERE rolname = 'quartz_jobstore_user') THEN
            CREATE ROLE quartz_jobstore_user WITH
                LOGIN
                NOSUPERUSER
                NOINHERIT
                NOCREATEDB
                NOCREATEROLE
                NOREPLICATION
                CONNECTION LIMIT 30
                PASSWORD '******';
        END IF;
    END
$do$;

CREATE SCHEMA IF NOT EXISTS "quartz";
GRANT USAGE ON SCHEMA "quartz" TO quartz_jobstore_user;
CREATE TABLE IF NOT EXISTS quartz.qrtz_job_details
(
    sched_name        text    NOT NULL,
    job_name          text    NOT NULL,
    job_group         text    NOT NULL,
    description       text,
    job_class_name    text    NOT NULL,
    is_durable        boolean NOT NULL,
    is_nonconcurrent  boolean NOT NULL,
    is_update_data    boolean NOT NULL,
    requests_recovery boolean NOT NULL,
    job_data          bytea
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_job_details TO quartz_jobstore_user;

CREATE TABLE IF NOT EXISTS quartz.qrtz_triggers
(
    sched_name     text   NOT NULL,
    trigger_name   text   NOT NULL,
    trigger_group  text   NOT NULL,
    job_name       text   NOT NULL,
    job_group      text   NOT NULL,
    description    text,
    next_fire_time bigint,
    prev_fire_time bigint,
    priority       integer,
    trigger_state  text   NOT NULL,
    trigger_type   text   NOT NULL,
    start_time     bigint NOT NULL,
    end_time       bigint,
    calendar_name  text,
    misfire_instr  smallint,
    job_data       bytea
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_triggers TO quartz_jobstore_user;

CREATE TABLE IF NOT EXISTS quartz.qrtz_simple_triggers
(
    sched_name      text   NOT NULL,
    trigger_name    text   NOT NULL,
    trigger_group   text   NOT NULL,
    repeat_count    bigint NOT NULL,
    repeat_interval bigint NOT NULL,
    times_triggered bigint NOT NULL
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_simple_triggers TO quartz_jobstore_user;

CREATE TABLE IF NOT EXISTS quartz.qrtz_simprop_triggers
(
    sched_name    text NOT NULL,
    trigger_name  text NOT NULL,
    trigger_group text NOT NULL,
    str_prop_1    text,
    str_prop_2    text,
    str_prop_3    text,
    int_prop_1    integer,
    int_prop_2    integer,
    long_prop_1   bigint,
    long_prop_2   bigint,
    dec_prop_1    numeric,
    dec_prop_2    numeric,
    bool_prop_1   boolean,
    bool_prop_2   boolean,
    time_zone_id  text
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_simprop_triggers TO quartz_jobstore_user;

CREATE TABLE IF NOT EXISTS quartz.qrtz_cron_triggers
(
    sched_name      text NOT NULL,
    trigger_name    text NOT NULL,
    trigger_group   text NOT NULL,
    cron_expression text NOT NULL,
    time_zone_id    text
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_cron_triggers TO quartz_jobstore_user;

CREATE TABLE IF NOT EXISTS quartz.qrtz_blob_triggers
(
    sched_name    text NOT NULL,
    trigger_name  text NOT NULL,
    trigger_group text NOT NULL,
    blob_data     bytea
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_blob_triggers TO quartz_jobstore_user;

CREATE TABLE IF NOT EXISTS quartz.qrtz_calendars
(
    sched_name    text  NOT NULL,
    calendar_name text  NOT NULL,
    calendar      bytea NOT NULL
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_calendars TO quartz_jobstore_user;

CREATE TABLE IF NOT EXISTS quartz.qrtz_paused_trigger_grps
(
    sched_name    text NOT NULL,
    trigger_group text NOT NULL
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_paused_trigger_grps TO quartz_jobstore_user;

CREATE TABLE IF NOT EXISTS quartz.qrtz_fired_triggers
(
    sched_name        text    NOT NULL,
    entry_id          text    NOT NULL,
    trigger_name      text    NOT NULL,
    trigger_group     text    NOT NULL,
    instance_name     text    NOT NULL,
    fired_time        bigint  NOT NULL,
    sched_time        bigint  NOT NULL,
    priority          integer NOT NULL,
    state             text    NOT NULL,
    job_name          text,
    job_group         text,
    is_nonconcurrent  boolean NOT NULL,
    requests_recovery boolean
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_fired_triggers TO quartz_jobstore_user;

CREATE TABLE IF NOT EXISTS quartz.qrtz_scheduler_state
(
    sched_name        text   NOT NULL,
    instance_name     text   NOT NULL,
    last_checkin_time bigint NOT NULL,
    checkin_interval  bigint NOT NULL
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_scheduler_state TO quartz_jobstore_user;

CREATE TABLE IF NOT EXISTS quartz.qrtz_locks
(
    sched_name text NOT NULL,
    lock_name  text NOT NULL
);

GRANT SELECT, INSERT, DELETE, UPDATE ON TABLE quartz.qrtz_locks TO quartz_jobstore_user;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_job_details
                ADD CONSTRAINT qrtz_job_details_pkey PRIMARY KEY (sched_name, job_name, job_group);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_job_details_pkey` for Table `quartz.qrtz_job_details` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_triggers
                ADD CONSTRAINT qrtz_triggers_pkey PRIMARY KEY (sched_name, trigger_name, trigger_group);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_triggers_pkey` for Table `quartz.qrtz_triggers` already exists';
        END;
    END
$$;


DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_triggers
                ADD CONSTRAINT qrtz_triggers_sched_name_job_name_job_group_fkey
                    FOREIGN KEY (sched_name, job_name, job_group)
                        REFERENCES quartz.qrtz_job_details (sched_name, job_name, job_group);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_triggers_sched_name_job_name_job_group_fkey` for Table `quartz.qrtz_triggers` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_simple_triggers
                ADD CONSTRAINT qrtz_simple_triggers_pkey PRIMARY KEY (sched_name, trigger_name, trigger_group);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_simple_triggers_pkey` for Table `quartz.qrtz_simple_triggers` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_simple_triggers
                ADD CONSTRAINT qrtz_simple_triggers_sched_name_trigger_name_trigger_group_fkey
                    FOREIGN KEY (sched_name, trigger_name, trigger_group)
                        REFERENCES quartz.qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE;
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_simple_triggers_sched_name_trigger_name_trigger_group_fkey` for Table `quartz.qrtz_simple_triggers` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_simprop_triggers
                ADD CONSTRAINT qrtz_simprop_triggers_pkey PRIMARY KEY (sched_name, trigger_name, trigger_group);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_simprop_triggers_pkey` for Table `quartz.qrtz_simprop_triggers` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_simprop_triggers
                ADD CONSTRAINT qrtz_simprop_triggers_sched_name_trigger_name_trigger_grou_fkey
                    FOREIGN KEY (sched_name, trigger_name, trigger_group)
                        REFERENCES quartz.qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE;
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_simprop_triggers_sched_name_trigger_name_trigger_grou_fkey` for Table `quartz.qrtz_simprop_triggers` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_cron_triggers
                ADD CONSTRAINT qrtz_cron_triggers_pkey PRIMARY KEY (sched_name, trigger_name, trigger_group);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_cron_triggers_pkey` for Table `quartz.qrtz_cron_triggers` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_cron_triggers
                ADD CONSTRAINT qrtz_cron_triggers_sched_name_trigger_name_trigger_group_fkey
                    FOREIGN KEY (sched_name, trigger_name, trigger_group)
                        REFERENCES quartz.qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE;
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_cron_triggers_sched_name_trigger_name_trigger_group_fkey` for Table `quartz.qrtz_cron_triggers` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_blob_triggers
                ADD CONSTRAINT qrtz_blob_triggers_pkey PRIMARY KEY (sched_name, trigger_name, trigger_group);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_blob_triggers_pkey` for Table `quartz.qrtz_blob_triggers` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_blob_triggers
                ADD CONSTRAINT qrtz_blob_triggers_sched_name_trigger_name_trigger_group_fkey
                    FOREIGN KEY (sched_name, trigger_name, trigger_group)
                        REFERENCES quartz.qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE;
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_blob_triggers_sched_name_trigger_name_trigger_group_fkey` for Table `quartz.qrtz_blob_triggers` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_calendars
                ADD CONSTRAINT qrtz_calendars_pkey PRIMARY KEY (sched_name, calendar_name);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_calendars_pkey` for Table `quartz.qrtz_calendars` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_paused_trigger_grps
                ADD CONSTRAINT qrtz_paused_trigger_grps_pkey PRIMARY KEY (sched_name, trigger_group);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_paused_trigger_grps_pkey` for Table `quartz.qrtz_paused_trigger_grps` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_fired_triggers
                ADD CONSTRAINT qrtz_fired_triggers_pkey PRIMARY KEY (sched_name, entry_id);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_fired_triggers_pkey` for Table `quartz.qrtz_fired_triggers` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_scheduler_state
                ADD CONSTRAINT qrtz_scheduler_state_pkey PRIMARY KEY (sched_name, instance_name);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_scheduler_state_pkey` for Table `quartz.qrtz_scheduler_state` already exists';
        END;
    END
$$;

DO
$$
    BEGIN
        BEGIN
            ALTER TABLE quartz.qrtz_locks
                ADD CONSTRAINT qrtz_locks_pkey PRIMARY KEY (sched_name, lock_name);
        EXCEPTION
            WHEN duplicate_table THEN
            WHEN duplicate_object THEN
            WHEN SQLSTATE '42P16' THEN
                RAISE NOTICE 'Constraint `qrtz_locks_pkey` for Table `quartz.qrtz_locks` already exists';
        END;
    END
$$;

CREATE INDEX IF NOT EXISTS idx_qrtz_j_req_recovery ON quartz.qrtz_job_details USING btree (requests_recovery);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_next_fire_time ON quartz.qrtz_triggers USING btree (next_fire_time);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_state ON quartz.qrtz_triggers USING btree (trigger_state);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_nft_st ON quartz.qrtz_triggers USING btree (next_fire_time, trigger_state);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_trig_name ON quartz.qrtz_fired_triggers USING btree (trigger_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_trig_group ON quartz.qrtz_fired_triggers USING btree (trigger_group);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_trig_nm_gp ON quartz.qrtz_fired_triggers USING btree (sched_name, trigger_name, trigger_group);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_trig_inst_name ON quartz.qrtz_fired_triggers USING btree (instance_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_job_name ON quartz.qrtz_fired_triggers USING btree (job_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_job_group ON quartz.qrtz_fired_triggers USING btree (job_group);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_job_req_recovery ON quartz.qrtz_fired_triggers USING btree (requests_recovery);
```
С использованием EF Core (с использованием пакета `appany.quartz.entityframeworkcore.migrations.postgresql`)
```csharp
public class DatabaseContext : DbContext
{
  // ...

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    // Prefix and schema can be passed as parameters
    
    modelBuilder.AddQuartz(builder => builder.UsePostgreSql());

  }
}
```
Затем примените миграцию с помощью `databaseContext.Database.MigrateAsync()` или `dotnet ef database update`

# Перезапуск задачи по ошибке
Пайплайн ReFireJobOnFailedBehavior позволяет повторно запустить задачу после задержки в случае возникновения исключения. По умолчанию задержка равна 10 секундам.<br>
Для регистрации пайплайна, добавьте в конфигурацию сервисов в классе Startup.cs
```csharp
// another pipelines...
services.AddPipeline(typeof(ReFireJobOnFailedBehavior<,>));
//...
```
Унаследуйте команду или запрос от интерфейса [IRequestReFireJobOnFailed](#)
И в случае необходимости переопределите метод [GetReFireJobOnErrorDelay](#), чтобы откорректировать значение задержки по умолчанию
```csharp
public class UploadSelloutGoodsCommand : IRequest, IRequestReFireJobOnFailed
{
    public TimeSpan GetReFireJobOnErrorDelay()
    {
        return TimeSpan.FromMilliseconds(20_000);
    }
}
```

# Регистрация конкурентной задачи
Чтобы зарегистрировать конкурентную задачу необходимо к классу **Handler** добавить атрибут **JobHandler** и в конструктор для параметра **isConcurrent** передать значение **true**

```csharp
    [JobHandler("SignDocuments", isConcurrent: true)]
    public class SignDocumentsCommandHandler : IRequestHandler<SignDocumentsCommand>
    {
        //... fields
    ]
```

# Получить список активных джобов с триггерами
Используйте метод **/jobs/list**, чтобы посмотреть зарегистрированные в сервисе джобы с информацией по триггерам. 
Например, метод http://api-dev.kifr-ru.local/quartzjobtest/jobs/list вернет 
список джобов, зарегестрированных в сервисе, и информацию по активным триггерам
```json
//Ответ, если у джоба есть активный триггер 
[
  {
    "jobKey": {
      "name": "TestJob",
      "group": "DEFAULT"
    },
    "triggers": [
      {
        "trigger": {
          "key": {
            "name": "TestJob",
            "group": "DEFAULT"
          },
          "jobKey": {
            "name": "TestJob",
            "group": "DEFAULT"
          },
          "description": null,
          "calendarName": null,
          "jobDataMap": {},
          "finalFireTimeUtc": null,
          "misfireInstruction": 0,
          "endTimeUtc": null,
          "startTimeUtc": "2023-08-16T15:09:22+00:00",
          "priority": 5,
          "hasMillisecondPrecision": false
        },
        "cronExpression": "0/10 * * ? * *",
        "triggerState": "Normal"
      }
    ]
  }
]

//Ответ, если у джоба нет активного триггера, соотв. джоб не выполняется периодически.
[
  {
    "jobKey": {
      "name": "TestJobTwo",
      "group": "DEFAULT"
    },
    "triggers": []
  }
]
```

# Добавление задачи
Чтобы добавить задачу для периодического выполнения, используется POST-метод **/jobs/{JobName}**. 
Метод регистрирует задачу(джоб) с именем **JobName**. В теле запроса можно указать группу и 
cron-выражение для джоба:
```json
{
  "jobGroup": "string",
  "cronExpression": "string"
}
```
# Восстановление триггера задачи из состояния Error
Чтобы восстановить триггер для задачи периодического выполнения, используется PUT-метод **/jobs/{JobName}/reset-from-error-state**.
Метод производит поиск триггера задачи(джобы) с именем **JobName**. В теле запроса можно указать группу для джоба:
```json
{
  "jobGroup": "string"
}
```
Если передать поля jobGroup и cronExpression пустыми, по умолчанию джоб регистрируется в группе 
**"DEFAULT"**, а cron-выражение берется из конфигурации appsettings.json:
```json
{
  "Jobs": {
    "SchedulerName": "TestJobScheduler",
    "TablePrefix": "quartz.qrtz_",
    "Triggers": {
      "TestJob": "0/5 * * ? * *"    <---------------- по умолчанию, cron-выражение берется отсюда
    }
  }
}
```
Чтобы переопределить значение Cron-выражения через CI/CD нужно добавить переменную в values.yaml:
```yaml
Jobs__Triggers__TestJob: "__Jobs_Triggers_TestJob_Cron__"
```
Соответственно, для DEV среды, CI/CD переменная будет такая:
```yaml
DEV_Jobs_Triggers_TestJob_Cron: 0/10 \* \* \? \* \*
```

# Отключение задачи
Чтобы остановить периодическое выполнение джоба, нужно выполнить DELETE-метод **/jobs/{JobName}**. 
При удалении можно указать группу джоба: ```?JobGroup=```. Это не обязательно, так как по умолчанию
в поле группы подставляется **"DEFAULT"**. После выполнения метода, для указанного джоба 
удаляется активный триггер, и джоб перестает выполняться периодически. Стоит учитывать, что 
после деплоя сервиса, триггеры автоматически регистрируются согласно конфигурации Jobs__Triggers.
```

# Принудительный вызов JobHandler'a с поддержкой Commands, содержащих данные
Принудительный запуск JobHandler'a возможен с помощью `IQuartzEnqueueManager`: 
```csharp
    ...
    this.enqueueManager.Enqueue(
                new SomeCommand
                {
                    Prop1 = prop1Value,
                    Prop2 = prop2Value,
                }, cancellationToken);
    ...
```

Передача данных в JobHandler работает за счет сериализации команды в один из ключей [JobData](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/more-about-jobs.html#jobdatamap) 

# Воссстановление упавших worker'ов
1. Существует механизм периодического поднятия worker'ов, триггеры которых находятся в состоянии ERROR. Он работает всегда, период проверки задает настройкой ```JobRecoveryDelayInMilliseconds```
2. Существует опциональный механизм поднятия worker'ов, триггеры которых зависли в состоянии BLOCKED. Для этого существует блок настроек:
```json
{
  "Jobs": {
    "BlockedJobsRecovery" : {
      "WorkersToRecover": [
        "ImportTreatmentFromDax",
        "ImportCommentsFromDax"
      ],
      "CheckIntervalInMilliseconds" : 5000,
      "MaxDelayBetweenLastFireTimeAndRecoverTimeInMilliseconds": 300000
    }
  }
}
```
где
```WorkersToRecover``` - worker'ы, за состоянием которых необходимо следить. Рекомендуется эмпирически определять этот набор, т.к. пересоздание каждого worker'a может привести к непредсказуемому поведению.
```CheckIntervalInMilliseconds``` - период проверки наличия BLOCKED-триггеров у worker'ов
```MaxDelayBetweenLastFireTimeAndRecoverTimeInMilliseconds``` - максимальная разница в мс между временем последнего запуска воркера и текущим временем. Если она будет превышена, то следует перезапустить worker.

# Подключение Admin UI для менеджмента тасков
Для подключения визуального интерфейса для просмотра/управления тасками, а также анализа ошибок при их выполнении необходимо:
1. Подключить AdminUI в Startup.cs 
```csharp
    ...
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env) 
	{
		...
		app.UseQuartzAdminUI(configuration);
		...
	|
    ...
```
2. Если необходима персистентная история запуска тасков, то необходимо дополнительно:
2.1. Указать необходимые настройки в `appsettings` в секции `Jobs`:
```csharp
    ...
    /// <summary>
    /// Подключение/отключение персистентной истории запуска заданий в Quartz AdminUi
    /// </summary>
    public bool EnableAdminUiPersistentJobHistory { get; set; }

    /// <summary>TTL записи в истории запуска заданий в Quartz AdminUI</summary>
    public int? PersistentRecentHistoryEntryTtl { get; set; }

    /// <summary>AdminUI URL</summary>
    public string AdminUiUrl { get; set; } = "/dashboard";
    
    /// <summary>
    /// AdminUI URL Prefix (если приложение развернуто не в root сайта)
    /// </summary>
    public string AdminUiUrlPrefix { get; set; }
    ...
```
2.2. Создать таблицы для хранения истории запусков в PostgreSQL с помощью скрипта:
```sql
	DROP TABLE IF EXISTS quartz.qrtz_execution_history_entries ;

	CREATE TABLE IF NOT EXISTS quartz.qrtz_execution_history_entries (
		fire_instance_id varchar(140) NOT NULL,
		scheduler_instance_id varchar(200) NOT NULL,
		sched_name varchar(120) NOT NULL,
		job_name varchar(150) NOT NULL,
		trigger_name varchar(150) NOT NULL,
		scheduled_fire_time_utc timestamp NULL,
		actual_fire_time_utc timestamp NOT NULL,
		recovering bool NOT NULL DEFAULT false,
		vetoed bool NOT NULL DEFAULT false,
		finished_time_utc timestamp NULL,
		exception_message text NULL,
		trial168 bpchar(1) NULL,
		CONSTRAINT pk_qrtz_execution_history_entries PRIMARY KEY (fire_instance_id)
	);
	CREATE INDEX ix_actual_fire_time_utc ON quartz.qrtz_execution_history_entries USING btree (actual_fire_time_utc);
	CREATE INDEX ix_job_name_actual_fire_time_utc ON quartz.qrtz_execution_history_entries USING btree (job_name, actual_fire_time_utc);
	CREATE INDEX ix_sched_name ON quartz.qrtz_execution_history_entries USING btree (sched_name);
	CREATE INDEX ix_trigger_name_actual_fire_time_utc ON quartz.qrtz_execution_history_entries USING btree (trigger_name, actual_fire_time_utc);

	GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_execution_history_entries TO quartz_jobstore_user;

	-- quartz.qrtz_execution_history_stats definition

	DROP TABLE IF EXISTS quartz.qrtz_execution_history_stats;

	CREATE TABLE IF NOT EXISTS quartz.qrtz_execution_history_stats (
		sched_name varchar(120) NOT NULL,
		stat_name varchar(120) NOT NULL,
		stat_value int8 NULL,
		trial171 bpchar(1) NULL,
		CONSTRAINT pk_execution_history_stats PRIMARY KEY (sched_name, stat_name)
	);
	
	GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_execution_history_stats TO quartz_jobstore_user;
```