<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

class AuditLog extends Model
{
    use HasFactory;

    protected $table = 'cms_audit_logs';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'actor_id',
        'actor',
        'action',
        'target',
        'timestamp',
        'ip',
    ];

    protected $casts = [
        'timestamp' => 'datetime',
    ];

    protected static function booted(): void
    {
        static::creating(function (AuditLog $log) {
            if (empty($log->id)) {
                $log->id = 'log_' . Str::lower(Str::random(12));
            }
            if (empty($log->timestamp)) {
                $log->timestamp = now();
            }
        });
    }
}
