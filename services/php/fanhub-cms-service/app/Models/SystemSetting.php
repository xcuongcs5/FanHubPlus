<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;

class SystemSetting extends Model
{
    use HasFactory;

    protected $table = 'cms_system_settings';

    protected $fillable = [
        'maintenance_mode',
        'platform_commission_fee',
        'max_upload_size_mb',
    ];

    protected $casts = [
        'maintenance_mode' => 'boolean',
        'platform_commission_fee' => 'float',
        'max_upload_size_mb' => 'integer',
    ];

    public static function current(): self
    {
        return static::firstOrCreate([], [
            'maintenance_mode' => false,
            'platform_commission_fee' => 5.0,
            'max_upload_size_mb' => 25,
        ]);
    }
}
